using ClipboardPal.Core.Models;

namespace ClipboardPal.Core.Abstractions;

public interface ISoundService
{
    void PlayCopySound(CopySoundKind kind);
}

public interface ITrayFeedbackService
{
    void SetVisible(bool visible);
    void AnimateCopy();
}

/// <summary>How a recognition attempt ended. "No text" and "engine missing" look the same to the
/// user unless they are told apart, which is the whole point of reporting an outcome.</summary>
public enum OcrOutcome
{
    Success,
    NoText,
    EngineUnavailable,
    Failed
}

/// <param name="Engine">Which backend produced the answer, for the settings diagnostics.</param>
/// <param name="Error">Why it failed, in plain text; null unless the outcome is a failure.</param>
public sealed record OcrResult(OcrOutcome Outcome, string? Text, string? Engine, string? Error)
{
    public static OcrResult Success(string text, string engine) =>
        new(OcrOutcome.Success, text, engine, null);

    public static OcrResult NoText(string engine) =>
        new(OcrOutcome.NoText, null, engine, null);

    public static OcrResult Unavailable(string error) =>
        new(OcrOutcome.EngineUnavailable, null, null, error);

    public static OcrResult Failed(string error) =>
        new(OcrOutcome.Failed, null, null, error);
}

/// <param name="Summary">Human-readable state of the engine, ready to show in settings.</param>
public sealed record OcrEngineInfo(bool IsReady, string Summary);

public interface IOcrService
{
    Task<OcrResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>Prepares the engine if needed and reports what it found, for the settings screen.</summary>
    Task<OcrEngineInfo> DescribeAsync(CancellationToken cancellationToken = default);
}

public interface ILinkPreviewService
{
    Task<LinkPreview?> FetchAsync(string url, CancellationToken cancellationToken = default);
}

public sealed record LinkPreview(string Url, string? Title, string? Description);

public interface IHotEdgeService : IAsyncDisposable
{
    event Action? EdgeTriggered;
}

public interface ILocalizationService
{
    string this[string key] { get; }
    void SetLanguage(UiLanguage language);
    event Action? LanguageChanged;
}
