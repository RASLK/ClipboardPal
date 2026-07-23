using CommunityToolkit.Mvvm.ComponentModel;
using ClipboardPal.Core.Services;

namespace ClipboardPal.Core.Models;

public sealed partial class AppSettings : ObservableObject
{
    // ----- Enable / monitor -----
    [ObservableProperty] private bool _clipboardMonitoringEnabled = true;

    // ----- General -----
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private bool _keepWindowOnTop = true;
    [ObservableProperty] private bool _showTrayIcon = true;
    [ObservableProperty] private PanelDock _panelDock = PanelDock.Bottom;
    [ObservableProperty] private bool _confirmBeforeQuit = true;
    [ObservableProperty] private double _panelHeight = 296;

    // ----- Copy & Paste -----
    [ObservableProperty] private bool _playSoundOnCopy;
    [ObservableProperty] private CopySoundKind _copySound = CopySoundKind.Default;
    [ObservableProperty] private bool _animateTrayOnCopy = true;
    [ObservableProperty] private bool _hidePopupAfterCopy = true;
    [ObservableProperty] private bool _hidePopupAfterPaste = true;
    [ObservableProperty] private bool _doubleClickEnabled = true;
    [ObservableProperty] private ClipAction _doubleClickAction = ClipAction.Paste;
    [ObservableProperty] private bool _quickSlotsEnabled = true;
    [ObservableProperty] private ClipAction _quickSlotAction = ClipAction.Paste;

    // Legacy alias used by older code paths
    public QuickSlotAction LegacyQuickSlotAction =>
        QuickSlotAction is ClipAction.Copy or ClipAction.CopyAsPlainText
            ? Models.QuickSlotAction.Copy
            : Models.QuickSlotAction.Paste;

    // ----- Appearance -----
    [ObservableProperty] private ThemeKind _theme = ThemeKind.Dark;
    [ObservableProperty] private UiLanguage _language = UiLanguage.English;
    [ObservableProperty] private bool _selectLastOnOpen = true;
    [ObservableProperty] private bool _moveRecentToTop = true;
    [ObservableProperty] private bool _showClipMenuButtons = true;
    [ObservableProperty] private int _mainWindowPageSize = 200;
    [ObservableProperty] private int _popupItemLimit = 200;
    [ObservableProperty] private int _previewLength = 400;

    // ----- Data -----
    [ObservableProperty] private bool _skipTrash = true;
    [ObservableProperty] private bool _recognizeTextOnImages;
    [ObservableProperty] private bool _saveDuplicates;
    [ObservableProperty] private bool _captureImages = true;
    [ObservableProperty] private string _excludedApps = string.Empty;
    [ObservableProperty] private int _maxHistoryItems = 300;

    // ----- Link -----
    [ObservableProperty] private bool _fetchLinkPreview;

    // ----- Search -----
    [ObservableProperty] private bool _searchCaseSensitive;
    [ObservableProperty] private bool _searchUseRegex;

    // ----- History -----
    [ObservableProperty] private HistoryRetention _historyRetention = HistoryRetention.Forever;

    // ----- Hot edge -----
    [ObservableProperty] private bool _hotEdgeEnabled;
    [ObservableProperty] private double _hotEdgeDelaySeconds = 0.2;

    // ----- Quick mode -----
    [ObservableProperty] private bool _quickModeEnabled;

    // ----- Queue -----
    [ObservableProperty] private bool _queueEnabled;
    [ObservableProperty] private QueuePasteMode _queuePasteMode = QueuePasteMode.Append;

    // ----- Plain text -----
    [ObservableProperty] private bool _autoPlainText;

    // ----- Permissions (informational / requested) -----
    [ObservableProperty] private bool _permissionAccessibility = true;
    [ObservableProperty] private bool _permissionScreenRecording;
    [ObservableProperty] private bool _permissionClipboardRead = true;

    // ----- Hotkey -----
    [ObservableProperty] private HotkeySpec _hotkey = new();

    /// <summary>Persisted schema version for forward migrations. Do not reset in ResetToDefaults.</summary>
    [ObservableProperty] private int _schemaVersion = AppDataPaths.CurrentSchemaVersion;

    /// <summary>Hide panel when clicking outside it.</summary>
    [ObservableProperty] private bool _hidePopupOnOutsideClick = true;

    public void ResetToDefaults()
    {
        ClipboardMonitoringEnabled = true;
        LaunchAtLogin = false;
        KeepWindowOnTop = true;
        ShowTrayIcon = true;
        PanelDock = PanelDock.Bottom;
        ConfirmBeforeQuit = true;
        PanelHeight = 296;

        PlaySoundOnCopy = false;
        CopySound = CopySoundKind.Default;
        AnimateTrayOnCopy = true;
        HidePopupAfterCopy = true;
        HidePopupAfterPaste = true;
        HidePopupOnOutsideClick = true;
        DoubleClickEnabled = true;
        DoubleClickAction = ClipAction.Paste;
        QuickSlotsEnabled = true;
        QuickSlotAction = ClipAction.Paste;

        Theme = ThemeKind.Dark;
        Language = UiLanguage.English;
        SelectLastOnOpen = true;
        MoveRecentToTop = true;
        ShowClipMenuButtons = true;
        MainWindowPageSize = 200;
        PopupItemLimit = 200;
        PreviewLength = 400;

        SkipTrash = true;
        RecognizeTextOnImages = false;
        SaveDuplicates = false;
        CaptureImages = true;
        ExcludedApps = string.Empty;
        MaxHistoryItems = 300;

        FetchLinkPreview = false;
        SearchCaseSensitive = false;
        SearchUseRegex = false;
        HistoryRetention = HistoryRetention.Forever;

        HotEdgeEnabled = false;
        HotEdgeDelaySeconds = 0.2;
        QuickModeEnabled = false;
        QueueEnabled = false;
        QueuePasteMode = QueuePasteMode.Append;
        AutoPlainText = false;

        PermissionAccessibility = true;
        PermissionScreenRecording = false;
        PermissionClipboardRead = true;

        Hotkey = new HotkeySpec();
        // SchemaVersion intentionally preserved / bumped on save.
    }

    public IReadOnlyList<string> ExcludedProcessNames() =>
        ExcludedApps
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static s => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s)
            .Where(static s => s.Length > 0)
            .Select(static s => s.ToLowerInvariant())
            .ToArray();
}
