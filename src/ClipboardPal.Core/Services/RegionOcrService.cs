using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using ClipboardPal.Core.ViewModels;

namespace ClipboardPal.Core.Services;

public sealed record RegionOcrOutcome(string? Text, string? Error)
{
    public static RegionOcrOutcome Done(string? text) => new(text, null);
    public static RegionOcrOutcome Failed(string error) => new(null, error);
}

/// <summary>
/// Reads text out of a part of an image the app already has. The recognized text goes to the
/// clipboard and into history as its own entry, while the picture's card remembers that it was
/// recognized so the badge can light up.
/// </summary>
public sealed class RegionOcrService
{
    private readonly IOcrService _ocr;
    private readonly IClipboardWatcher _clipboard;
    private readonly HistoryStore _store;
    private readonly MainViewModel _main;
    private readonly IUiDispatcher _dispatcher;

    public RegionOcrService(
        IOcrService ocr,
        IClipboardWatcher clipboard,
        HistoryStore store,
        MainViewModel main,
        IUiDispatcher dispatcher)
    {
        _ocr = ocr;
        _clipboard = clipboard;
        _store = store;
        _main = main;
        _dispatcher = dispatcher;
    }

    /// <param name="item">The card whose picture was cropped, updated with the result.</param>
    /// <param name="croppedPng">The selected region, already cut out of the source image.</param>
    public async Task<RegionOcrOutcome> RecognizeAsync(
        ClipItem item,
        byte[] croppedPng,
        CancellationToken cancellationToken = default)
    {
        _dispatcher.Post(() => item.OcrStatus = OcrStatus.Running);

        // The engines read from disk, and the crop is not worth keeping around afterwards.
        var scratch = Path.Combine(Path.GetTempPath(), $"clipboardpal-region-{Guid.NewGuid():N}.png");
        try
        {
            await File.WriteAllBytesAsync(scratch, croppedPng, cancellationToken).ConfigureAwait(false);
            var result = await _ocr.RecognizeAsync(scratch, cancellationToken).ConfigureAwait(false);
            var text = result.Outcome == OcrOutcome.Success ? result.Text?.Trim() : null;

            if (string.IsNullOrWhiteSpace(text))
            {
                var failed = result.Outcome is OcrOutcome.EngineUnavailable or OcrOutcome.Failed;
                _dispatcher.Post(() =>
                {
                    item.OcrStatus = failed ? OcrStatus.Failed : OcrStatus.NoText;
                    item.OcrError = result.Error;
                });
                return failed
                    ? RegionOcrOutcome.Failed(result.Error ?? "recognition failed")
                    : RegionOcrOutcome.Done(null);
            }

            await _clipboard.SetTextAsync(text, cancellationToken).ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                item.OcrText = text;
                item.OcrError = null;
                item.OcrStatus = OcrStatus.Done;

                // Added here rather than left to the clipboard watcher, which is muted for a
                // moment after the app writes to the clipboard itself.
                _main.AddCaptured(new ClipItem
                {
                    Type = ClipItemType.Text,
                    Text = text,
                    Hash = HistoryStore.Sha256Text(text),
                    SourceApp = "ClipboardPal"
                });
            });

            return RegionOcrOutcome.Done(text);
        }
        catch (OperationCanceledException)
        {
            _dispatcher.Post(() => item.OcrStatus = OcrStatus.None);
            throw;
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() =>
            {
                item.OcrStatus = OcrStatus.Failed;
                item.OcrError = ex.Message;
            });
            return RegionOcrOutcome.Failed(ex.Message);
        }
        finally
        {
            try { File.Delete(scratch); } catch { /* ignore */ }
        }
    }
}
