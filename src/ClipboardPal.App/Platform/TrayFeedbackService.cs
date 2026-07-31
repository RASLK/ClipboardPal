using Avalonia.Controls;
using ClipboardPal.Core.Abstractions;

namespace ClipboardPal.Platform;

public sealed class TrayFeedbackService : ITrayFeedbackService
{
    private TrayIcon? _tray;
    private string _baseTip = "ClipboardPal";
    private CancellationTokenSource? _animCts;

    public void Attach(TrayIcon tray)
    {
        _tray = tray;
        _baseTip = tray.ToolTipText ?? "ClipboardPal";
    }

    public void SetVisible(bool visible)
    {
        if (_tray is not null)
            _tray.IsVisible = visible;
    }

    /// <summary>
    /// Shows a short-lived message in the tray tooltip. Used by flows that have no window open
    /// to report into, such as screen-region recognition.
    /// </summary>
    public void ShowMessage(string message, int seconds = 6)
    {
        if (_tray is null) return;

        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        var token = _animCts.Token;

        _tray.ToolTipText = message;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), token).ConfigureAwait(false);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_tray is not null)
                        _tray.ToolTipText = _baseTip;
                });
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer message.
            }
        }, token);
    }

    public void AnimateCopy()
    {
        if (_tray is null) return;

        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        var token = _animCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 3 && !token.IsCancellationRequested; i++)
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_tray is not null)
                            _tray.ToolTipText = i % 2 == 0 ? "● ClipboardPal" : _baseTip;
                    });
                    await Task.Delay(120, token).ConfigureAwait(false);
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_tray is not null)
                        _tray.ToolTipText = _baseTip;
                });
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }, token);
    }
}
