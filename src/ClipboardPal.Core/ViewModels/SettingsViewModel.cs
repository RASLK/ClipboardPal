using System.Collections.ObjectModel;
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
    private readonly IUpdateService _updates;

    private UpdateCheck? _pendingUpdate;

    public AppSettings Settings => _settingsService.Settings;
    public LocView Loc { get; }

    public string AppVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0.0";

    public string AppVersionFull => $"v{AppVersion}";

    public ObservableCollection<SelectableSourceApp> SourceAppChoices { get; } = [];

    [ObservableProperty]
    private string _hotkeyDisplay = string.Empty;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private bool _isCapturingHotkey;

    public SettingsViewModel(
        SettingsService settingsService,
        MainViewModel main,
        IAutostartService autostart,
        IGlobalHotkeyService hotkeys,
        ILocalizationService l10n,
        IUpdateService updates)
    {
        _settingsService = settingsService;
        _main = main;
        _autostart = autostart;
        _hotkeys = hotkeys;
        _l10n = l10n;
        _updates = updates;
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
            if (e.PropertyName == nameof(AppSettings.ExcludedApps))
                RefreshSourceAppChoices();
        };
    }

    /// <summary>
    /// Rebuilds the distinct source-app list for the exclusions picker (from history + trash).
    /// Apps already on the exclusion list are left out - re-adding them is pointless.
    /// </summary>
    public void RefreshSourceAppChoices()
    {
        var excluded = new HashSet<string>(Settings.ExcludedProcessNames(), StringComparer.OrdinalIgnoreCase);

        var apps = _main.Items
            .Concat(_main.TrashItems)
            .Select(static i => i.SourceApp)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(static s => s!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static s => s, StringComparer.OrdinalIgnoreCase)
            .Where(name => !excluded.Contains(NormalizeProcessName(name)))
            .ToList();

        SourceAppChoices.Clear();
        foreach (var app in apps)
            SourceAppChoices.Add(new SelectableSourceApp(app));

        OnPropertyChanged(nameof(HasSourceAppChoices));
    }

    public bool HasSourceAppChoices => SourceAppChoices.Count > 0;

    /// <summary>Adds every checked app in <see cref="SourceAppChoices"/> to the exclusion list at once.</summary>
    [RelayCommand]
    private void AddSelectedSourceApps()
    {
        var selected = SourceAppChoices.Where(static a => a.IsSelected).Select(static a => a.Name).ToList();
        if (selected.Count == 0)
            return;

        var current = Settings.ExcludedApps;
        foreach (var name in selected)
        {
            var normalized = NormalizeProcessName(name);
            if (Settings.ExcludedProcessNames().Contains(normalized))
                continue;
            current = AppendExcluded(current, name);
        }

        Settings.ExcludedApps = current;
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
        IsCapturingHotkey = false;
        HotkeyDisplay = Settings.Hotkey.ToString();
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        UpdateAvailable = false;
        _pendingUpdate = null;
        UpdateStatus = _l10n["upd.checking"];
        try
        {
            var check = await _updates.CheckAsync().ConfigureAwait(true);
            if (!check.UpdateAvailable)
            {
                UpdateStatus = string.Format(_l10n["upd.uptodate"], check.Current.ToString(3));
                return;
            }

            _pendingUpdate = check;
            UpdateStatus = string.Format(_l10n["upd.available"], check.Latest.ToString(3));
            UpdateAvailable = true;
        }
        catch (Exception ex)
        {
            UpdateStatus = $"{_l10n["upd.error"]}{Environment.NewLine}{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate is not { } update)
            return;

        if (!update.CanAutoInstall)
        {
            // A dev/portable run has no matching release package — hand over to the browser.
            UpdateStatus = _l10n["upd.manual"];
            _updates.OpenReleasePage(update);
            return;
        }

        UpdateAvailable = false;
        try
        {
            var progress = new Progress<int>(p => UpdateStatus = string.Format(_l10n["upd.downloading"], p));
            await _updates.InstallAsync(update, progress).ConfigureAwait(true);
            UpdateStatus = _l10n["upd.restart"];
        }
        catch (Exception ex)
        {
            UpdateStatus = $"{_l10n["upd.error"]}{Environment.NewLine}{ex.Message}";
            UpdateAvailable = true;
        }
    }

    [RelayCommand]
    private async Task ClearAllHistoryAsync()
    {
        await _main.ClearAllHistoryAsync();
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

    private static string NormalizeProcessName(string name)
    {
        var s = name.Trim();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            s = s[..^4];
        return s.ToLowerInvariant();
    }

    private static string AppendExcluded(string current, string app)
    {
        if (string.IsNullOrWhiteSpace(current))
            return app;

        var trimmed = current.TrimEnd();
        var sep = trimmed.Contains('\n') || trimmed.Contains('\r') ? "\n" : ", ";
        return trimmed + sep + app;
    }
}
