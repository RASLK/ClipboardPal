using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ClipboardPal.Core.Abstractions;
using SkiaSharp;

namespace ClipboardPal.Platform;

public sealed class TrayFeedbackService : ITrayFeedbackService
{
    private const int ShakeOffsetPx = 3;
    private const int ShakeStepMs = 55;

    private static readonly Uri TrayAssetUri = new("avares://ClipboardPal/Assets/tray.png");

    private TrayIcon? _tray;
    private string _baseTip = "ClipboardPal";
    private CancellationTokenSource? _animCts;
    private int _animGeneration;

    private WindowIcon? _iconOriginal;
    private WindowIcon? _iconLeft;
    private WindowIcon? _iconRight;
    private bool _shakeIconsReady;

    public void Attach(TrayIcon tray)
    {
        _tray = tray;
        _baseTip = tray.ToolTipText ?? "ClipboardPal";
        _iconOriginal = tray.Icon;
        EnsureShakeIcons();
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

        // Invalidate any in-flight copy shake so its finally does not fight us.
        Interlocked.Increment(ref _animGeneration);
        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        var token = _animCts.Token;

        RestoreOriginalIcon();
        _tray.ToolTipText = message;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), token).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_tray is not null)
                        _tray.ToolTipText = _baseTip;
                });
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer message or shake.
            }
        }, token);
    }

    public void AnimateCopy()
    {
        if (_tray is null) return;

        var generation = Interlocked.Increment(ref _animGeneration);
        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        var token = _animCts.Token;

        EnsureShakeIcons();

        _ = Task.Run(async () =>
        {
            try
            {
                if (_shakeIconsReady
                    && _iconLeft is not null
                    && _iconRight is not null
                    && _iconOriginal is not null)
                {
                    // left → center → right → center, twice — short physical-looking nudge.
                    WindowIcon?[] frames =
                    [
                        _iconLeft, _iconOriginal, _iconRight, _iconOriginal,
                        _iconLeft, _iconOriginal, _iconRight, _iconOriginal,
                    ];

                    foreach (var frame in frames)
                    {
                        if (token.IsCancellationRequested || generation != _animGeneration)
                            break;

                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                if (_tray is not null && frame is not null)
                                    _tray.Icon = frame;
                            }
                            catch
                            {
                                // Some hosts may reject rapid Icon swaps; degrade silently.
                            }
                        });
                        await Task.Delay(ShakeStepMs, token).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Fallback when shifted bitmaps could not be built.
                    for (var i = 0; i < 3 && !token.IsCancellationRequested; i++)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (_tray is not null)
                                _tray.ToolTipText = i % 2 == 0 ? "● ClipboardPal" : _baseTip;
                        });
                        await Task.Delay(120, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Replaced by a newer animation/message.
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation != _animGeneration)
                        return;

                    RestoreOriginalIcon();
                    if (_tray is not null)
                        _tray.ToolTipText = _baseTip;
                });
            }
        }, token);
    }

    private void EnsureShakeIcons()
    {
        if (_shakeIconsReady)
            return;

        try
        {
            using var stream = AssetLoader.Open(TrayAssetUri);
            using var source = SKBitmap.Decode(stream)
                ?? throw new InvalidOperationException("Failed to decode tray.png");

            _iconLeft = CreateShiftedIcon(source, -ShakeOffsetPx, 0);
            _iconRight = CreateShiftedIcon(source, ShakeOffsetPx, 0);
            _iconOriginal ??= CreateShiftedIcon(source, 0, 0);
            _shakeIconsReady = true;
        }
        catch
        {
            _shakeIconsReady = false;
        }
    }

    private static WindowIcon CreateShiftedIcon(SKBitmap source, int offsetX, int offsetY)
    {
        var info = new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, offsetX, offsetY);
        }

        using var image = SKImage.FromBitmap(target);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Failed to encode shifted tray icon");
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    private void RestoreOriginalIcon()
    {
        if (_tray is null || _iconOriginal is null)
            return;

        try
        {
            _tray.Icon = _iconOriginal;
        }
        catch
        {
            // Ignore platform limitations when restoring.
        }
    }
}
