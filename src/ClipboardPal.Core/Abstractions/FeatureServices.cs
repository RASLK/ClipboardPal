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

public interface IOcrService
{
    Task<string?> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
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
