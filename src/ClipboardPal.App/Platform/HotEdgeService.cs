using System.Runtime.InteropServices;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using SharpHook.Native;

namespace ClipboardPal.Platform;

/// <summary>Shows the panel when the cursor rests on a screen edge (Win / macOS / Linux).</summary>
public sealed class HotEdgeService : IHotEdgeService
{
    private readonly AppSettings _settings;
    private readonly IUiDispatcher _dispatcher;
    private readonly IPointerTracker _pointer;
    private readonly CancellationTokenSource _cts = new();

    private DateTime? _edgeSince;

    public event Action? EdgeTriggered;

    public HotEdgeService(AppSettings settings, IUiDispatcher dispatcher, IPointerTracker pointer)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _pointer = pointer;
        _ = LoopAsync(_cts.Token);
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!_settings.HotEdgeEnabled)
            {
                _edgeSince = null;
                continue;
            }

            if (!TryGetCursor(out var x, out var y))
                continue;

            var onCorner = IsOnConfiguredCorner(x, y, _settings.HotEdgeCorner);
            if (!onCorner)
            {
                _edgeSince = null;
                continue;
            }

            _edgeSince ??= DateTime.UtcNow;
            var delay = TimeSpan.FromSeconds(Math.Clamp(_settings.HotEdgeDelaySeconds, 0.05, 5));
            if (DateTime.UtcNow - _edgeSince >= delay)
            {
                _edgeSince = DateTime.UtcNow.AddYears(1); // prevent retrigger until leave corner
                _dispatcher.Post(() => EdgeTriggered?.Invoke());
            }
        }
    }

    private bool TryGetCursor(out int x, out int y)
    {
        if (_pointer.TryGetPosition(out x, out y))
            return true;

        if (OperatingSystem.IsWindows() && GetCursorPos(out var p))
        {
            x = p.X;
            y = p.Y;
            return true;
        }

        x = y = 0;
        return false;
    }

    private static bool IsOnConfiguredCorner(int x, int y, HotEdgeCorner corner)
    {
        // Small square hit-zone at the chosen screen corner (virtual desktop bounds).
        const int zone = 10;
        if (!TryGetVirtualScreen(out var left, out var top, out var width, out var height))
            return false;

        var right = left + width - 1;
        var bottom = top + height - 1;

        return corner switch
        {
            HotEdgeCorner.TopLeft => x <= left + zone && y <= top + zone,
            HotEdgeCorner.TopRight => x >= right - zone && y <= top + zone,
            HotEdgeCorner.BottomLeft => x <= left + zone && y >= bottom - zone,
            HotEdgeCorner.BottomRight => x >= right - zone && y >= bottom - zone,
            _ => x >= right - zone && y >= bottom - zone
        };
    }

    private static bool TryGetVirtualScreen(out int left, out int top, out int width, out int height)
    {
        left = top = width = height = 0;
        try
        {
            var screens = UioHook.CreateScreenInfo();
            if (screens is { Length: > 0 })
            {
                left = screens.Min(s => s.X);
                top = screens.Min(s => s.Y);
                var right = screens.Max(s => s.X + s.Width);
                var bottom = screens.Max(s => s.Y + s.Height);
                width = right - left;
                height = bottom - top;
                return width > 0 && height > 0;
            }
        }
        catch
        {
            // fall through
        }

        if (OperatingSystem.IsWindows())
        {
            left = GetSystemMetrics(76);
            top = GetSystemMetrics(77);
            width = GetSystemMetrics(78);
            height = GetSystemMetrics(79);
            return width > 0 && height > 0;
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
