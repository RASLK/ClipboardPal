using System.Reflection;

namespace ClipboardPal.Platform.Ocr;

/// <summary>How one backend's attempt ended, before the service decides what to do next.</summary>
public enum OcrEngineOutcome
{
    Success,
    /// <summary>Ran fine, the image simply has no readable text — do not try the next engine.</summary>
    NoText,
    /// <summary>Not usable on this machine — move on to the next engine.</summary>
    Unavailable,
    Failed
}

public sealed record OcrEngineResult(OcrEngineOutcome Outcome, string? Text, string? Error)
{
    public static OcrEngineResult Success(string text) => new(OcrEngineOutcome.Success, text, null);
    public static OcrEngineResult NoText() => new(OcrEngineOutcome.NoText, null, null);
    public static OcrEngineResult Unavailable(string error) => new(OcrEngineOutcome.Unavailable, null, error);
    public static OcrEngineResult Failed(string error) => new(OcrEngineOutcome.Failed, null, error);
}

/// <summary>
/// One way of reading text off an image. Every engine either ships with the OS or inside this
/// application — none of them may ask the user to install or download anything.
/// </summary>
public interface IOcrEngine
{
    string Name { get; }

    /// <summary>Whether this engine can run on the current OS at all.</summary>
    bool IsSupported { get; }

    Task<OcrEngineResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken);
}

/// <summary>
/// Stable error tokens that may travel from an engine up to the UI. When a token matches a
/// localization key, the service layer can swap it for a user-facing sentence.
/// </summary>
public static class OcrErrorKeys
{
    public const string NoLanguagePack = "ocr.error.noLanguagePack";
    public const string Failed = "ocr.error.failed";
    public const string Unavailable = "ocr.error.unavailable";
}

internal static class OcrText
{
    public static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? text;
        return line.Length <= 200 ? line : line[..200] + "…";
    }

    /// <summary>Unwraps to the exception that actually says something.</summary>
    public static string Describe(Exception ex) =>
        ex is AggregateException or TargetInvocationException && ex.InnerException is { } inner
            ? Describe(inner)
            : $"{ex.GetType().Name}: {ex.Message}";
}
