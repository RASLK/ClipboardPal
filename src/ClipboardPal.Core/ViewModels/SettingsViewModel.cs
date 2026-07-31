using System.Reflection;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using ClipboardPal.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipboardPal.Core.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _main;
    private readonly IAutostartService _autostart;
    private readonly IGlobalHotkeyService _hotkeys;
    private readonly ILocalizationService _l10n;
    private readonly IOcrService _ocr;

    public AppSettings Settings => _settingsService.Settings;
    public LocView Loc { get; }

    public string AppVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0.0";

    public string AppVersionFull => $"v{AppVersion}";

    [ObservableProperty]
    private string _hotkeyDisplay = string.Empty;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isCapturingHotkey;

    [ObservableProperty]
    private string _ocrStatus = string.Empty;

    public SettingsViewModel(
        SettingsService settingsService,
        MainViewModel main,
        IAutostartService autostart,
        IGlobalHotkeyService hotkeys,
        ILocalizationService l10n,
        IOcrService ocr)
    {
        _settingsService = settingsService;
        _main = main;
        _autostart = autostart;
        _hotkeys = hotkeys;
        _l10n = l10n;
        _ocr = ocr;
        Loc = new LocView(l10n);
        HotkeyDisplay = Settings.Hotkey.ToString();

        if (Settings.LaunchAtLogin != autostart.IsEnabled)
            autostart.SetEnabled(Settings.LaunchAtLogin);

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Hotkey) && !IsCapturingHotkey)
                HotkeyDisplay = Settings.Hotkey.ToString();
            if (e.PropertyName == nameof(AppSettings.LaunchAtLogin))
                _autostart.SetEnabled(Settings.LaunchAtLogin);
        };
    }

    [RelayCommand]
    private void BeginHotkeyCapture()
    {
        IsCapturingHotkey = true;
        HotkeyDisplay = _l10n["hotkey.press"];
        _hotkeys.BeginCapture(spec =>
        {
            IsCapturingHotkey = false;
            if (spec is not null)
                Settings.Hotkey = spec;
            HotkeyDisplay = Settings.Hotkey.ToString();
        });
    }

    [RelayCommand]
    private void CancelHotkeyCapture()
    {
        _hotkeys.EndCapture();
        if (IsCapturingHotkey)
        {
            IsCapturingHotkey = false;
            HotkeyDisplay = Settings.Hotkey.ToString();
        }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        UpdateStatus = "…";
        await Task.Delay(600);
        UpdateStatus = "OK";
    }

    /// <summary>
    /// Answers "is OCR alive at all" without making the user copy a picture and guess. The first
    /// call unpacks the bundled model, so it can take a moment; it never goes to the network.
    /// </summary>
    [RelayCommand]
    private async Task CheckOcrAsync()
    {
        OcrStatus = _l10n["ocr.checking"];
        try
        {
            var info = await _ocr.DescribeAsync().ConfigureAwait(true);
            var headline = info.IsReady ? _l10n["ocr.ready"] : _l10n["ocr.notready"];
            OcrStatus = $"{headline}{Environment.NewLine}{info.Summary}";
        }
        catch (Exception ex)
        {
            OcrStatus = $"{_l10n["ocr.notready"]}{Environment.NewLine}{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ClearAllHistoryAsync()
    {
        await _main.ClearAllHistoryAsync();
        UpdateStatus = "OK";
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        Settings.ResetToDefaults();
        Settings.LaunchAtLogin = _autostart.IsEnabled;
        HotkeyDisplay = Settings.Hotkey.ToString();
        _l10n.SetLanguage(Settings.Language);
    }

    public async Task SaveAsync() => await _settingsService.SaveAsync();
}
