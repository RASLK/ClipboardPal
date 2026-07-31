using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardPal.Core.Models;

public sealed partial class ClipItem : ObservableObject
{
    public static int GlobalPreviewLength { get; set; } = 400;

    /// <summary>Set at startup so type names follow the UI language.</summary>
    public static Func<string, string>? Localize { get; set; }

    private static string L(string key, string fallback) => Localize?.Invoke(key) ?? fallback;

    private string? _titleBeforeEdit;

    /// <summary>Enter edit mode remembering the current title so Esc can revert it.</summary>
    public void BeginEdit()
    {
        _titleBeforeEdit = Title;
        IsEditing = true;
    }

    /// <summary>Leave edit mode discarding changes made since <see cref="BeginEdit"/>.</summary>
    public void CancelEdit()
    {
        Title = _titleBeforeEdit;
        IsEditing = false;
    }

    /// <summary>
    /// Re-reads the card text that comes from <see cref="Localize"/> and from the relative-time
    /// converter instead of a binding, so a language switch reaches cards that are already shown.
    /// </summary>
    public void RefreshLocalizedText()
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(CreatedAt));
        OnPropertyChanged(nameof(OcrTooltip));
        OnPropertyChanged(nameof(QueueTooltip));
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    public ClipItemType Type { get; set; }

    public string? Text { get; set; }

    public string? ImagePath { get; set; }

    public string Hash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? SourceApp { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOcr))]
    [NotifyPropertyChangedFor(nameof(DisplayOcrStatus))]
    [NotifyPropertyChangedFor(nameof(OcrTooltip))]
    private string? _ocrText;

    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(DisplayOcrStatus))]
    [NotifyPropertyChangedFor(nameof(IsOcrRunning))]
    [NotifyPropertyChangedFor(nameof(IsOcrFailed))]
    [NotifyPropertyChangedFor(nameof(OcrTooltip))]
    private OcrStatus _ocrStatus;

    [ObservableProperty]
    [property: JsonIgnore]
    [NotifyPropertyChangedFor(nameof(OcrTooltip))]
    private string? _ocrError;

    public string? LinkUrl { get; set; }

    public string? LinkPreviewTitle { get; set; }

    public string? LinkPreviewDescription { get; set; }

    public bool IsInTrash { get; set; }

    public DateTime? TrashedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayTitle))]
    private string? _title;

    [ObservableProperty]
    private bool _isPinned;

    /// <summary>Persisted so the staged queue survives a restart (rebuilt from this flag on load).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QueueTooltip))]
    private bool _isQueued;

    /// <summary>
    /// When the clip was staged. Persisted separately from <see cref="CreatedAt"/> — which
    /// MarkUsed rewrites on every paste — so the queue keeps its staging order across restarts.
    /// </summary>
    public DateTime? QueuedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlotBadge))]
    [property: JsonIgnore]
    private int _slotNumber;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isEditing;

    [ObservableProperty]
    [property: JsonIgnore]
    private bool _isSelected;

    [JsonIgnore]
    public string SlotBadge => SlotNumber == 10 ? "0" : SlotNumber.ToString();

    [JsonIgnore]
    public bool HasOcr => !string.IsNullOrWhiteSpace(OcrText);

    /// <summary>
    /// State to display. Live state wins; a clip reloaded from disk has no live state, so text
    /// that survived the restart still reads as recognized.
    /// </summary>
    [JsonIgnore]
    public OcrStatus DisplayOcrStatus =>
        OcrStatus != OcrStatus.None ? OcrStatus :
        HasOcr ? OcrStatus.Done : OcrStatus.None;

    /// <summary>The OCR button only makes sense on cards that hold a picture.</summary>
    [JsonIgnore]
    public bool IsImage => Type == ClipItemType.Image;

    [JsonIgnore]
    public bool IsOcrRunning => DisplayOcrStatus == OcrStatus.Running;

    [JsonIgnore]
    public bool IsOcrFailed => DisplayOcrStatus == OcrStatus.Failed;

    [JsonIgnore]
    public string? OcrTooltip => DisplayOcrStatus switch
    {
        OcrStatus.Running => L("ocr.tip.running", "Recognizing text…"),
        OcrStatus.Done => OcrText,
        OcrStatus.NoText => L("ocr.tip.notext", "No readable text on this image"),
        OcrStatus.Failed => OcrError ?? L("ocr.tip.failed", "Text recognition failed"),
        _ => L("ocr.tip.pick", "Select a part of the image to read its text")
    };

    [JsonIgnore]
    public string QueueTooltip => IsQueued
        ? L("tip.queue.remove", "Remove from queue")
        : L("tip.queue.add", "Add to queue");

    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title!;
            if (!string.IsNullOrWhiteSpace(LinkPreviewTitle)) return LinkPreviewTitle!;
            if (!string.IsNullOrWhiteSpace(SourceApp)) return SourceApp!;
            return Type == ClipItemType.Text ? L("type.text", "Text") : L("type.image", "Image");
        }
    }

    [JsonIgnore]
    public string PreviewText
    {
        get
        {
            var t = (Text ?? OcrText ?? LinkPreviewDescription ?? string.Empty).TrimStart('\r', '\n');
            return t.Length <= GlobalPreviewLength ? t : t[..GlobalPreviewLength];
        }
    }
}
