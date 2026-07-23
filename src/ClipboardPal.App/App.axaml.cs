using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using ClipboardPal.Core.Services;
using ClipboardPal.Core.ViewModels;
using ClipboardPal.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace ClipboardPal;

public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private MainViewModel? _viewModel;
    private IGlobalHotkeyService? _hotkeys;
    private IClipboardWatcher? _clipboard;
    private IHotEdgeService? _hotEdge;
    private ILocalizationService? _l10n;
    private SettingsService? _settingsService;
    private TrayFeedbackService? _trayFeedback;
    private Mutex? _singleInstance;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _singleInstance = new Mutex(true, @"Local\ClipboardPal.SingleInstance.v2", out var created);
        if (!created)
        {
            // Don't call Shutdown() here — it tears down the dispatcher before MainLoop starts.
            Environment.Exit(0);
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        _settingsService = _services.GetRequiredService<SettingsService>();
        _l10n = _services.GetRequiredService<ILocalizationService>();
        _l10n.SetLanguage(_settingsService.Settings.Language);
        _l10n.LanguageChanged += ApplyTrayLocalization;
        ApplyTheme(_settingsService.Settings.Theme);
        ApplyTrayLocalization();

        _settingsService.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.Theme))
                ApplyTheme(_settingsService.Settings.Theme);
            if (e.PropertyName == nameof(AppSettings.Language))
                _l10n.SetLanguage(_settingsService.Settings.Language);
            if (e.PropertyName == nameof(AppSettings.ShowTrayIcon))
                _trayFeedback?.SetVisible(_settingsService.Settings.ShowTrayIcon);
        };

        _viewModel = _services.GetRequiredService<MainViewModel>();
        _clipboard = _services.GetRequiredService<IClipboardWatcher>();
        _hotkeys = _services.GetRequiredService<IGlobalHotkeyService>();
        _hotEdge = _services.GetRequiredService<IHotEdgeService>();
        _trayFeedback = _services.GetRequiredService<TrayFeedbackService>();

        if (TrayIcon.GetIcons(this) is { Count: > 0 } icons)
        {
            _trayFeedback.Attach(icons[0]);
            _trayFeedback.SetVisible(_settingsService.Settings.ShowTrayIcon);
            ApplyTrayLocalization();
        }

        _clipboard.ItemCaptured += item =>
            Dispatcher.UIThread.Post(() => _viewModel.AddCaptured(item));

        _hotkeys.HotkeyPressed += () =>
            Dispatcher.UIThread.Post(() => _mainWindow?.TogglePanel());

        _hotkeys.QuickModeCycle += () =>
            Dispatcher.UIThread.Post(() =>
            {
                _mainWindow?.ShowPanel();
                _viewModel.QuickModeCycle();
            });

        _hotkeys.QuickModeCommit += () =>
            Dispatcher.UIThread.Post(() =>
            {
                if (_viewModel.QuickModeCurrent() is { } item)
                    _mainWindow?.CommitClip(item);
                _viewModel.QuickModeReset();
            });

        _hotEdge.EdgeTriggered += () =>
            Dispatcher.UIThread.Post(() => _mainWindow?.ShowPanel());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow(
                _viewModel,
                _settingsService.Settings,
                _clipboard,
                _hotkeys,
                _services.GetRequiredService<IPasteService>(),
                OpenSettingsWindow);

            desktop.MainWindow = _mainWindow;
            _mainWindow.AttachHotkeys(_hotkeys);
            desktop.ShutdownRequested += async (_, _) => await ShutdownServicesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher>(_ => new AvaloniaUiDispatcher(Dispatcher.UIThread));
        services.AddSingleton<HistoryStore>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton(sp => sp.GetRequiredService<SettingsService>().Settings);
        services.AddSingleton<ClipQueueService>();
        services.AddSingleton<ILinkPreviewService, LinkPreviewService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<TrayFeedbackService>();
        services.AddSingleton<ITrayFeedbackService>(sp => sp.GetRequiredService<TrayFeedbackService>());
        services.AddSingleton<IOcrService, PlatformOcrService>();
        services.AddSingleton<IHotEdgeService, HotEdgeService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IForegroundAppService, ProcessForegroundAppService>();
        services.AddSingleton<IPasteService, PlatformPasteService>();
        services.AddSingleton<IAutostartService, PlatformAutostartService>();
        services.AddSingleton<IClipboardWatcher, AvaloniaClipboardWatcher>();
        services.AddSingleton<SharpHookHotkeyService>();
        services.AddSingleton<IGlobalHotkeyService>(sp => sp.GetRequiredService<SharpHookHotkeyService>());
        services.AddSingleton<IPointerTracker>(sp => sp.GetRequiredService<SharpHookHotkeyService>());
        services.AddTransient<SettingsViewModel>();
    }

    public void ApplyTheme(ThemeKind theme)
    {
        var effective = theme switch
        {
            ThemeKind.System => ActualThemeVariant == ThemeVariant.Light
                ? ThemeKind.Light
                : ThemeKind.Dark,
            _ => theme
        };

        var uri = effective == ThemeKind.Dark
            ? "avares://ClipboardPal/Themes/Dark.axaml"
            : "avares://ClipboardPal/Themes/Light.axaml";

        if (Resources.MergedDictionaries.Count > 0)
            Resources.MergedDictionaries[0] = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri))!;
        else
            Resources.MergedDictionaries.Add((ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri))!);

        RequestedThemeVariant = theme switch
        {
            ThemeKind.Light => ThemeVariant.Light,
            ThemeKind.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void ApplyTrayLocalization()
    {
        void Apply()
        {
            if (_l10n is null) return;
            if (TrayIcon.GetIcons(this) is not { Count: > 0 } icons) return;
            if (icons[0].Menu is not { } menu) return;

            var labeled = menu.Items.OfType<NativeMenuItem>().ToList();
            if (labeled.Count < 4) return;
            labeled[0].Header = _l10n["tray.open"];
            labeled[1].Header = _l10n["tray.settings"];
            labeled[2].Header = _l10n["tray.clear"];
            labeled[3].Header = _l10n["tray.exit"];
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    public void OpenSettingsWindow()
    {
        if (_services is null) return;

        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_services.GetRequiredService<SettingsViewModel>());
        _settingsWindow.Show();
    }

    private void Tray_Clicked(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _mainWindow?.TogglePanel());

    private void TrayOpen_Click(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _mainWindow?.ShowPanel());

    private void TraySettings_Click(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(OpenSettingsWindow);

    private void TrayClear_Click(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _viewModel?.ClearUnpinnedCommand.Execute(null));

    private async void TrayExit_Click(object? sender, EventArgs e)
    {
        if (_settingsService?.Settings.ConfirmBeforeQuit == true)
        {
            var confirmed = await ConfirmQuitAsync();
            if (!confirmed) return;
        }

        await ShutdownServicesAsync();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private Task<bool> ConfirmQuitAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        var owner = _mainWindow;
        var l10n = _l10n;

        var yes = new Button { Content = l10n?["quit.yes"] ?? "Выйти", Padding = new Thickness(14, 6) };
        var no = new Button { Content = l10n?["quit.no"] ?? "Отмена", Padding = new Thickness(14, 6) };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { no, yes }
        };
        var dialog = new Window
        {
            Title = "ClipboardPal",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = l10n?["quit.confirm"] ?? "Закрыть ClipboardPal?",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    buttons
                }
            }
        };

        yes.Click += (_, _) => { tcs.TrySetResult(true); dialog.Close(); };
        no.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
        dialog.Closed += (_, _) => tcs.TrySetResult(false);

        if (owner is not null)
            _ = dialog.ShowDialog(owner);
        else
            dialog.Show();

        return tcs.Task;
    }

    private async Task ShutdownServicesAsync()
    {
        if (_viewModel is not null)
            await _viewModel.DisposeAsync();
        if (_settingsService is not null)
            await _settingsService.DisposeAsync();
        if (_hotkeys is not null)
            await _hotkeys.DisposeAsync();
        if (_clipboard is not null)
            await _clipboard.DisposeAsync();
        if (_hotEdge is not null)
            await _hotEdge.DisposeAsync();
        _services?.Dispose();
        _singleInstance?.Dispose();
    }
}
