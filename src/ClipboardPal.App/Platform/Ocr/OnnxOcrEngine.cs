using Microsoft.ML.OnnxRuntime;
using RapidOcrNet;
using SkiaSharp;

namespace ClipboardPal.Platform.Ocr;

/// <summary>
/// PP-OCRv5 running on ONNX Runtime — the engine that makes OCR work everywhere, including
/// Linux, where the OS offers nothing. Models are embedded in this assembly and unpacked once
/// into the app's data directory, so a single-file build needs no companion files and no network.
/// The recognition model covers Cyrillic and Latin alphabets with one set of weights.
/// </summary>
public sealed class OnnxOcrEngine : IOcrEngine
{
    private static readonly string[] ModelFiles = ["det.onnx", "cls.onnx", "rec.onnx", "keys.txt"];

    private readonly string _modelDir;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private RapidOcr? _ocr;
    private string? _initError;

    public OnnxOcrEngine(string modelDirectory) => _modelDir = modelDirectory;

    public string Name => "PP-OCRv5 (built-in)";

    public bool IsSupported => true;

    public async Task<OcrEngineResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken)
    {
        var ocr = await GetOcrAsync(cancellationToken).ConfigureAwait(false);
        if (ocr is null)
            return OcrEngineResult.Unavailable(_initError ?? "engine unavailable");

        return await Task.Run(() =>
        {
            try
            {
                using var raw = SKBitmap.Decode(imagePath);
                if (raw is null)
                    return OcrEngineResult.Failed("could not decode the image");

                // The models expect BGRA; screenshots decode to whatever the file happens to use.
                using var bitmap = ToBgra(raw);
                var text = (ocr.Detect(bitmap, RapidOcrOptions.Default).StrRes ?? string.Empty).Trim();
                return text.Length > 0 ? OcrEngineResult.Success(text) : OcrEngineResult.NoText();
            }
            catch (Exception ex)
            {
                return OcrEngineResult.Failed(OcrText.Describe(ex));
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static SKBitmap ToBgra(SKBitmap source)
    {
        if (source.ColorType == SKColorType.Bgra8888)
            return source.Copy();

        var bitmap = new SKBitmap(new SKImageInfo(
            source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawBitmap(source, 0, 0);
        return bitmap;
    }

    private async Task<RapidOcr?> GetOcrAsync(CancellationToken cancellationToken)
    {
        if (_ocr is not null)
            return _ocr;

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ocr is not null)
                return _ocr;

            await ExtractModelsAsync(cancellationToken).ConfigureAwait(false);

            QuietenOnnxLogging();

            var options = new SessionOptions
            {
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
            };

            var ocr = new RapidOcr();
            ocr.InitModels(
                Path.Combine(_modelDir, "det.onnx"),
                Path.Combine(_modelDir, "cls.onnx"),
                Path.Combine(_modelDir, "rec.onnx"),
                Path.Combine(_modelDir, "keys.txt"),
                options);
            _ocr = ocr;
            _initError = null;
            return _ocr;
        }
        catch (Exception ex)
        {
            _initError = OcrText.Describe(ex);
            return null;
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// ONNX Runtime otherwise prints a screenful of "unused initializer" warnings for these
    /// converted models on every load. The environment carries the level, so it has to be created
    /// before the first session — after that the call is a no-op.
    /// </summary>
    private static void QuietenOnnxLogging()
    {
        try
        {
            var options = new EnvironmentCreationOptions
            {
                logId = "ClipboardPal",
                logLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
            };
            OrtEnv.CreateInstanceWithOptions(ref options);
        }
        catch
        {
            // Already created elsewhere — the session options below still apply.
        }
    }

    /// <summary>Writes the embedded models out once; later runs reuse what is already there.</summary>
    private async Task ExtractModelsAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_modelDir);
        var assembly = typeof(OnnxOcrEngine).Assembly;

        foreach (var name in ModelFiles)
        {
            var target = Path.Combine(_modelDir, name);
            var resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith($".{name}", StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"embedded model {name} is missing");

            await using var source = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"embedded model {name} cannot be read");

            // A partially written model from a previous crash would fail to load, so size is
            // the check that decides whether the file on disk is the one we shipped.
            if (File.Exists(target) && new FileInfo(target).Length == source.Length)
                continue;

            var partial = target + ".part";
            await using (var file = File.Create(partial))
                await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            File.Move(partial, target, overwrite: true);
        }
    }

    /// <summary>Reports readiness without running a recognition pass.</summary>
    public async Task<string?> ProbeAsync(CancellationToken cancellationToken)
    {
        var ocr = await GetOcrAsync(cancellationToken).ConfigureAwait(false);
        return ocr is null ? _initError ?? "engine unavailable" : null;
    }
}
