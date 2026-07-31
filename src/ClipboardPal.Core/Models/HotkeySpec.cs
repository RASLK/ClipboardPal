using System.Text.Json.Serialization;

namespace ClipboardPal.Core.Models;

public enum ThemeKind
{
    Dark,
    Light,
    System
}

public enum PanelDock
{
    Bottom,
    Top,
    Left,
    Right
}

public enum ClipAction
{
    Copy,
    CopyAsPlainText,
    Paste,
    PasteAsPlainText
}

public enum HistoryRetention
{
    Day,
    Week,
    Month,
    ThreeMonths,
    SixMonths,
    Year,
    Forever
}

public enum UiLanguage
{
    English = 0,
    Russian = 1,
    German = 2,
    French = 3,
    Chinese = 4,
    Japanese = 5
}

public enum CopySoundKind
{
    Default,
    Soft,
    Click,
    Pop
}

/// <summary>Which clip space the panel is showing.</summary>
public enum PanelSpace
{
    History,
    Queue,
    Trash
}

public enum ClipItemType
{
    Text,
    Image
}

/// <summary>What happened to an image clip's text recognition, as shown on the card.</summary>
public enum OcrStatus
{
    /// <summary>Not attempted — OCR is off, or the clip is not an image.</summary>
    None,
    Running,
    Done,
    /// <summary>The engine worked, the image just has no readable text.</summary>
    NoText,
    Failed
}

public enum QuickSlotAction
{
    Paste,
    Copy
}

/// <summary>Keyboard chord: modifiers + primary key name (A–Z, D0–D9, F1…). Platform-agnostic.</summary>
public sealed class HotkeySpec
{
    /// <summary>Win on Windows, Cmd on macOS, Super on Linux.</summary>
    [JsonIgnore]
    public bool Meta { get; set; } = true;

    public bool Shift { get; set; } = true;
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public string KeyName { get; set; } = "V";

    [JsonIgnore]
    public bool HasModifier => Meta || Shift || Ctrl || Alt;

    /// <summary>Persisted as Win for compatibility with the WPF settings.json.</summary>
    public bool Win
    {
        get => Meta;
        set => Meta = value;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Meta) parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add(OperatingSystem.IsMacOS() ? "Option" : "Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(KeyLabel(KeyName));
        return string.Join(" + ", parts);
    }

    public string ModifiersLabel()
    {
        var parts = new List<string>(4);
        if (Meta) parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add(OperatingSystem.IsMacOS() ? "Option" : "Alt");
        if (Shift) parts.Add("Shift");
        return string.Join(" + ", parts);
    }

    private static string KeyLabel(string name) =>
        name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]) ? name[1..] : name;
}
