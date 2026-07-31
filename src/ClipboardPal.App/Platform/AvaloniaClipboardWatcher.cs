using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using ClipboardPal.Core.Services;

namespace ClipboardPal.Platform;

/// <summary>
/// Clipboard watcher: Win32 listener + polling backup on Windows; polling elsewhere.
/// Clipboard reads always run on the Avalonia UI thread.
/// </summary>
public sealed class AvaloniaClipboardWatcher : IClipboardWatcher
{
    private readonly HistoryStore _store;
    private readonly AppSettings _settings;
    private readonly IForegroundAppService _foreground;
    private readonly IUiDispatcher _dispatcher;
    private readonly CancellationTokenSource _cts = new();

    private DateTime _suppressUntil = DateTime.MinValue;
    private string? _lastHash;
    private object? _windowsListener;
    private int _captureBusy;

    /// <summary>
    /// The bitmap currently promised to the OS clipboard. See <see cref="SetImageAsync"/>.
    /// </summary>
    private Bitmap? _clipboardBitmap;

    public event Action<ClipItem>? ItemCaptured;

    public AvaloniaClipboardWatcher(
        HistoryStore store,
        AppSettings settings,
        IForegroundAppService foreground,
        IUiDispatcher dispatcher)
    {
        _store = store;
        _settings = settings;
        _foreground = foreground;
        _dispatcher = dispatcher;

        if (OperatingSystem.IsWindows())
        {
            var listener = new WindowsClipboardListener(OnOsClipboardChanged);
            listener.Start();
            _windowsListener = listener;
        }

        // Polling as primary on Unix and reliable backup on Windows.
        _ = PollLoopAsync(_cts.Token);
    }

    public void SuppressFor(TimeSpan duration) =>
        _suppressUntil = DateTime.UtcNow + duration;

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        SuppressFor(TimeSpan.FromMilliseconds(800));
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = GetClipboard();
            if (clipboard is null) return;
            await clipboard.SetTextAsync(text).ConfigureAwait(false);
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts an image on the clipboard.
    /// The bitmap must outlive this call: platforms hand the clipboard a promise and only encode
    /// the pixels when something actually reads them (on macOS <c>pasteboardPropertyListForType:</c>
    /// calls back into <c>Bitmap.Save</c> at paste time). Disposing it here would fault the process
    /// on the next paste, so the instance is released only once a newer image has replaced it.
    /// </summary>
    public async Task SetImageAsync(byte[] pngBytes, CancellationToken cancellationToken = default)
    {
        SuppressFor(TimeSpan.FromMilliseconds(800));
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = GetClipboard();
            if (clipboard is null) return;

            Bitmap bitmap;
            using (var ms = new MemoryStream(pngBytes))
                bitmap = new Bitmap(ms);

            var previous = _clipboardBitmap;
            _clipboardBitmap = bitmap;

            try
            {
                await clipboard.SetBitmapAsync(bitmap).ConfigureAwait(true);
            }
            catch
            {
                // The clipboard still holds whatever was there before.
                _clipboardBitmap = previous;
                bitmap.Dispose();
                throw;
            }

            // The clipboard has a new owner now, so the previous promise is never queried again.
            // Both this and the promise callback run on the UI thread, so they cannot interleave.
            previous?.Dispose();
        }).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            QueueCapture();
    }

    private void OnOsClipboardChanged() => QueueCapture();

    private void QueueCapture()
    {
        if (Interlocked.CompareExchange(ref _captureBusy, 1, 0) != 0)
            return;

        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await CaptureOnUiAsync().ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref _captureBusy, 0);
            }
        });
    }

    private async Task CaptureOnUiAsync()
    {
        try
        {
            // Give the source app a moment to finish writing the clipboard.
            await Task.Delay(40).ConfigureAwait(true);

            if (DateTime.UtcNow < _suppressUntil)
                return;

            if (!_settings.ClipboardMonitoringEnabled || !_settings.PermissionClipboardRead)
                return;

            string? text = null;
            for (var attempt = 0; attempt < 3 && text is null; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(50 * attempt).ConfigureAwait(true);

                text = await TryReadTextAsync().ConfigureAwait(true);
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                var hash = HistoryStore.Sha256Text(text);
                if (hash == _lastHash) return;
                // Recorded before the exclusion check so the same content never re-triggers the
                // source-app lookup on every poll tick. Cost: content skipped because an excluded
                // app was frontmost stays skipped until the clipboard changes, even if the user
                // copies it again from an allowed app. Re-checking instead would capture excluded
                // content as soon as the user switched apps, which is the worse failure.
                _lastHash = hash;

                var sourceApp = await GetSourceAppAsync().ConfigureAwait(true);
                if (IsExcluded(sourceApp))
                    return;

                ItemCaptured?.Invoke(new ClipItem
                {
                    Type = ClipItemType.Text,
                    Text = text,
                    Hash = hash,
                    SourceApp = sourceApp
                });
                return;
            }

            if (!_settings.CaptureImages)
                return;

            var png = await TryReadClipboardPngAsync().ConfigureAwait(true);
            if (png is not { Length: > 0 })
                return;

            var imageHash = HistoryStore.Sha256Hex(png);
            if (imageHash == _lastHash) return;
            _lastHash = imageHash;

            var imageSourceApp = await GetSourceAppAsync().ConfigureAwait(true);
            if (IsExcluded(imageSourceApp))
                return;

            var path = _store.NewImagePath();
            await File.WriteAllBytesAsync(path, png, _cts.Token).ConfigureAwait(true);

            ItemCaptured?.Invoke(new ClipItem
            {
                Type = ClipItemType.Image,
                ImagePath = path,
                Hash = imageHash,
                SourceApp = imageSourceApp
            });
        }
        catch
        {
            // Clipboard busy / unsupported format.
        }
    }

    /// <summary>
    /// Resolving the frontmost app spawns a helper process on macOS/Linux (osascript/xdotool)
    /// and can take hundreds of milliseconds — never run it on the UI thread.
    /// </summary>
    private Task<string?> GetSourceAppAsync() =>
        Task.Run(_foreground.GetForegroundProcessName);

    private bool IsExcluded(string? sourceApp) =>
        sourceApp is not null &&
        _settings.ExcludedProcessNames().Contains(sourceApp.ToLowerInvariant());

    private async Task<string?> TryReadTextAsync()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var winText = WindowsClipboardText.TryGetUnicodeText();
                if (!string.IsNullOrWhiteSpace(winText))
                    return winText;
            }

            var clipboard = GetClipboard();
            if (clipboard is null)
                return null;

            return await clipboard.TryGetTextAsync().ConfigureAwait(true);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> TryReadClipboardPngAsync()
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard is null)
                return null;

            var bitmap = await clipboard.TryGetBitmapAsync().ConfigureAwait(true);
            if (bitmap is null)
                return null;

            await using var ms = new MemoryStream();
            bitmap.Save(ms);
            bitmap.Dispose();
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard
                   ?? desktop.Windows.FirstOrDefault()?.Clipboard;
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
        (_windowsListener as IDisposable)?.Dispose();
        // _clipboardBitmap is deliberately left alone: the OS may still ask for its bytes while
        // the process shuts down, and the memory goes away with the process anyway.
    }
}

[SupportedOSPlatform("windows")]
file static class WindowsClipboardText
{
    private const uint CF_UNICODETEXT = 13;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint hMem);

    public static string? TryGetUnicodeText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            return null;

        if (!OpenClipboard(0))
            return null;

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
                return null;

            var ptr = GlobalLock(handle);
            if (ptr == 0)
                return null;

            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }
}

[SupportedOSPlatform("windows")]
file sealed class WindowsClipboardListener : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(nint hwnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    private readonly Action _onChanged;
    private readonly WndProc _proc;
    private nint _hwnd;
    private Thread? _thread;
    private volatile bool _running;

    public WindowsClipboardListener(Action onChanged)
    {
        _onChanged = onChanged;
        _proc = Callback;
    }

    public void Start()
    {
        _running = true;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "ClipboardPal.ClipboardListener"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void MessageLoop()
    {
        var className = "ClipboardPalClipboardListener";
        var wndClass = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = GetModuleHandleW(null),
            lpszClassName = className
        };
        RegisterClassW(ref wndClass);
        _hwnd = CreateWindowExW(0, className, string.Empty, 0, 0, 0, 0, 0, 0, 0, wndClass.hInstance, 0);
        AddClipboardFormatListener(_hwnd);

        while (_running)
        {
            if (!GetMessage(out var msg, 0, 0, 0))
                break;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private nint Callback(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            _onChanged();
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("user32.dll")]
    private static extern bool GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    public void Dispose()
    {
        _running = false;
        if (_hwnd != 0)
        {
            RemoveClipboardFormatListener(_hwnd);
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }
    }
}
