using ClipboardPal.Core.Models;

namespace ClipboardPal.Core.Abstractions;

/// <summary>Watches OS clipboard and raises captures as <see cref="ClipItem"/>.</summary>
public interface IClipboardWatcher : IAsyncDisposable
{
    event Action<ClipItem>? ItemCaptured;

    /// <summary>Ignore clipboard changes for a short window (self-writes).</summary>
    void SuppressFor(TimeSpan duration);

    Task SetTextAsync(string text, CancellationToken cancellationToken = default);

    Task SetImageAsync(byte[] pngBytes, CancellationToken cancellationToken = default);

}

public interface IGlobalHotkeyService : IAsyncDisposable
{
    bool IsInstalled { get; }
    bool PanelVisible { get; set; }

    event Action? HotkeyPressed;
    event Action<int>? QuickSlotPressed;
    event Action<bool>? QuickChordChanged;
    event Action? QuickModeCycle;
    event Action? QuickModeCommit;

    /// <summary>Screen-pixel coordinates of a primary mouse button press.</summary>
    event Action<int, int>? MousePressed;

    void BeginCapture(Action<HotkeySpec?> onCaptured);
    void EndCapture();
    void RecheckChord();
}

public interface IPasteService
{
    nint CaptureForegroundWindow();
    Task PasteToAsync(nint targetWindow, CancellationToken cancellationToken = default);
}

public interface IAutostartService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public interface IForegroundAppService
{
    string? GetForegroundProcessName();
}

/// <summary>Last known global pointer position (updated by the hotkey hook).</summary>
public interface IPointerTracker
{
    bool TryGetPosition(out int x, out int y);
}

public interface IUiDispatcher
{
    void Post(Action action);
    Task InvokeAsync(Action action);
    /// <summary>True when the caller is already on the UI thread.</summary>
    bool CheckAccess();
}
