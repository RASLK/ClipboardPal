using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Services;
using ClipboardPal.Platform.Ocr;

namespace ClipboardPal.Platform;

/// <summary>
/// Recognizes text on images using only what is already available: the OS engine where there is
/// one (Apple Vision on macOS, Windows OCR on Windows) and an embedded PP-OCRv5 model everywhere
/// else. Nothing is installed, nothing is downloaded, and the app works offline on first run.
/// </summary>
public sealed class PlatformOcrService : IOcrService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IOcrEngine[] _engines;
    private readonly ILocalizationService _l10n;

    public PlatformOcrService(HistoryStore store, ILocalizationService l10n)
    {
        _l10n = l10n;

        // System engines first: they are faster, need no unpacking, and follow the user's
        // installed languages. The built-in model is the safety net that always works.
        var engines = new List<IOcrEngine>();
        if (OperatingSystem.IsMacOS())
            engines.Add(new AppleVisionOcrEngine());
        if (OperatingSystem.IsWindows())
            engines.Add(new WindowsMediaOcrEngine());
        engines.Add(new OnnxOcrEngine(Path.Combine(store.RootDirectory, "ocr", "models")));

        _engines = engines.Where(e => e.IsSupported).ToArray();
    }

    public async Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return OcrResult.Failed($"Image file not found: {imagePath}");

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var problems = new List<string>();

            foreach (var engine in _engines)
            {
                var result = await engine.RecognizeAsync(imagePath, cancellationToken).ConfigureAwait(false);
                switch (result.Outcome)
                {
                    case OcrEngineOutcome.Success when !string.IsNullOrWhiteSpace(result.Text):
                        return OcrResult.Success(result.Text, engine.Name);

                    // A working engine that found nothing is an answer, not a reason to retry.
                    case OcrEngineOutcome.NoText:
                    case OcrEngineOutcome.Success:
                        return OcrResult.NoText(engine.Name);

                    case OcrEngineOutcome.Unavailable:
                    case OcrEngineOutcome.Failed:
                        problems.Add($"{engine.Name} — {Localize(result.Error)}");
                        break;

                    default:
                    {
                        // Exhaustive for future enum members.
                        problems.Add($"{engine.Name} — {Localize(result.Error)}");
                        break;
                    }
                }
            }

            return problems.Count == 0
                ? OcrResult.Unavailable(Localize(OcrErrorKeys.Unavailable))
                : OcrResult.Failed(string.Join("; ", problems));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OcrResult.Failed(OcrText.Describe(ex));
        }
        finally
        {
            Gate.Release();
        }
    }

    private string Localize(string? tokenOrMessage)
    {
        if (string.IsNullOrWhiteSpace(tokenOrMessage))
            return _l10n[OcrErrorKeys.Failed];

        var localized = _l10n[tokenOrMessage];
        return localized == tokenOrMessage ? tokenOrMessage : localized;
    }
}
