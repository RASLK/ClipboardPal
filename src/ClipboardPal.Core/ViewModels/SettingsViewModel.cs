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

    public SettingsViewModel(
        SettingsService settingsService,
        MainViewModel main,
        IAutostartService autostart,
        IGlobalHotkeyService hotkeys,
        ILocalizationService l10n)
    {
        _settingsService = settingsService;
        _main = main;
        _autostart = autostart;
        _hotkeys = hotkeys;
        _l10n = l10n;
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
