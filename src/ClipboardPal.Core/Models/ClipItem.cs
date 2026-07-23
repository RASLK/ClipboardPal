using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardPal.Core.Models;

public sealed partial class ClipItem : ObservableObject
{
    public static int GlobalPreviewLength { get; set; } = 400;

    public Guid Id { get; set; } = Guid.NewGuid();

    public ClipItemType Type { get; set; }

    public string? Text { get; set; }

    public string? ImagePath { get; set; }

    public string Hash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? SourceApp { get; set; }

    public string? OcrText { get; set; }

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
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title!;
            if (!string.IsNullOrWhiteSpace(LinkPreviewTitle)) return LinkPreviewTitle!;
            if (!string.IsNullOrWhiteSpace(SourceApp)) return SourceApp!;
            return Type == ClipItemType.Text ? "Текст" : "Изображение";
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

    [JsonIgnore]
    public string Meta
    {
        get
        {
            if (Type == ClipItemType.Image)
                return string.IsNullOrWhiteSpace(OcrText) ? "Изображение" : "Изображение · OCR";
            if (!string.IsNullOrWhiteSpace(LinkUrl))
                return "Ссылка";
            return $"{Text?.Length ?? 0} симв.";
        }
    }
}
