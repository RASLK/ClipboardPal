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
    private bool _shutdownStarted;

    /// <summary>
    /// Localization key carried by a tray menu item. Labels used to be assigned by position,
    /// but <see cref="NativeMenuItemSeparator"/> derives from <see cref="NativeMenuItem"/>, so
    /// separators counted as items and shifted every caption by one — the entry reading "Quit"
    /// was really "Clear history". An explicit key cannot drift.
    /// </summary>
    public static readonly AttachedProperty<string?> MenuKeyProperty =
        AvaloniaProperty.RegisterAttached<App, NativeMenuItem, string?>("MenuKey");

    public static string? GetMenuKey(NativeMenuItem item) => item.GetValue(MenuKeyProperty);

    public static void SetMenuKey(NativeMenuItem item, string? value) => item.SetValue(MenuKeyProperty, value);

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
        RelativeTimeConverter.Localize = key => _l10n[key];
        ClipItem.Localize = key => _l10n[key];
        ApplyTheme(_settingsService.Settings.Theme);
        ActualThemeVariantChanged += (_, _) =>
        {
            if (_settingsService?.Settings.Theme == ThemeKind.System)
                ApplyThemeDictionary(ActualThemeVariant == ThemeVariant.Light ? ThemeKind.Light : ThemeKind.Dark);
        };
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
                OpenSettingsWindow,
                _services.GetRequiredService<RegionOcrService>(),
                _l10n);

            desktop.MainWindow = _mainWindow;
            _mainWindow.AttachHotkeys(_hotkeys);
            desktop.ShutdownRequested += (_, e) =>
            {
                // Cmd+Q, the system Quit item and OS logout land here without passing through
                // the tray handler. The handler is not awaited by the lifetime, so an async
                // teardown here would be cut short — intercept once, run the full quit path,
                // and let the Shutdown() it issues pass through on the second visit.
                if (_shutdownStarted)
                    return;
                e.Cancel = true;
                Dispatcher.UIThread.Post(() => _ = QuitAsync(askConfirm: false));
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IUpdateService>(sp => new GitHubUpdateService(
            sp.GetRequiredService<HistoryStore>(),
            () => Dispatcher.UIThread.InvokeAsync(() => QuitAsync(askConfirm: false))));
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
        services.AddSingleton<RegionOcrService>();
        services.AddSingleton<SharpHookHotkeyService>();
        services.AddSingleton<IGlobalHotkeyService>(sp => sp.GetRequiredService<SharpHookHotkeyService>());
        services.AddSingleton<IPointerTracker>(sp => sp.GetRequiredService<SharpHookHotkeyService>());
        services.AddTransient<SettingsViewModel>();
    }

    public void ApplyTheme(ThemeKind theme)
    {
        // Set the variant first: for System it resolves to the OS theme, and the
        // brush dictionary below must follow the resolved value, not the stale one.
        RequestedThemeVariant = theme switch
        {
            ThemeKind.Light => ThemeVariant.Light,
            ThemeKind.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        var effective = theme switch
        {
            ThemeKind.System => ActualThemeVariant == ThemeVariant.Light
                ? ThemeKind.Light
                : ThemeKind.Dark,
            _ => theme
        };

        ApplyThemeDictionary(effective);
    }

    private void ApplyThemeDictionary(ThemeKind effective)
    {
        var uri = effective == ThemeKind.Dark
            ? "avares://ClipboardPal/Themes/Dark.axaml"
            : "avares://ClipboardPal/Themes/Light.axaml";

        var dictionary = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri))!;
        if (Resources.MergedDictionaries.Count > 0)
            Resources.MergedDictionaries[0] = dictionary;
        else
            Resources.MergedDictionaries.Add(dictionary);
    }

    private void ApplyTrayLocalization()
    {
        void Apply()
        {
            if (_l10n is null) return;
            if (TrayIcon.GetIcons(this) is not { Count: > 0 } icons) return;
            if (icons[0].Menu is not { } menu) return;

            foreach (var item in menu.Items.OfType<NativeMenuItem>())
            {
                if (GetMenuKey(item) is { Length: > 0 } key)
                    item.Header = _l10n[key];
            }
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
        // An agent app (no Dock icon) is not brought forward by the OS — ask explicitly, or the
        // window opens behind whatever the user was working in.
        _settingsWindow.Activate();
    }

    private void Tray_Clicked(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _mainWindow?.TogglePanel());

    private void TrayOpen_Click(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _mainWindow?.ShowPanel());

    private void TraySettings_Click(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(OpenSettingsWindow);

    private void TrayClear_Click(object? sender, EventArgs e) =>
        // Not ClearUnpinnedCommand: that clears whatever space the panel last showed, and the
        // tray item promises "Clear history" regardless of which tab was left open.
        Dispatcher.UIThread.Post(() => _viewModel?.ClearHistoryUnpinned());

    private async void TrayExit_Click(object? sender, EventArgs e) =>
        await QuitAsync(askConfirm: true);

    private async Task QuitAsync(bool askConfirm)
    {
        if (_shutdownStarted)
            return;

        if (askConfirm && _settingsService?.Settings.ConfirmBeforeQuit == true)
        {
            bool confirmed;
            try
            {
                confirmed = await ConfirmQuitAsync();
            }
            catch (Exception ex)
            {
                // A confirmation that cannot be shown must never become a quit that cannot
                // happen — that is exactly how the tray item ended up doing nothing.
                LogQuitProblem("ConfirmQuit", ex);
                confirmed = true;
            }

            if (!confirmed || _shutdownStarted)
                return;
        }

        _shutdownStarted = true;

        // Armed BEFORE teardown: stopping the global hook can hang in native code (uiohook on
        // macOS), and the hook runs on a foreground thread that would keep a dead-looking
        // process alive. A quit must mean quit, so the hard stop cannot depend on the teardown
        // finishing.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Environment.Exit(0);
        });

        await ShutdownServicesAsync();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private Task<bool> ConfirmQuitAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
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
            // The tray is the only thing on screen when this appears — it has to come to front.
            Topmost = true,
            ShowInTaskbar = true,
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

        // Deliberately ownerless. The panel is the only candidate owner and it spends its life
        // hidden off-screen; ShowDialog rejects a non-visible owner outright ("Cannot show
        // window with non-visible owner"), and that exception — swallowed inside the async void
        // tray handler — is why pressing Quit appeared to do nothing at all.
        dialog.Show();
        dialog.Activate();

        return tcs.Task;
    }

    private static void LogQuitProblem(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClipboardPal");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:O}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never be the reason a quit fails.
        }
    }

    private async Task ShutdownServicesAsync()
    {
        // Every service gets its turn even when an earlier one fails: one bad dispose must not
        // leave the hook thread alive or the history unsaved.
        await TryAsync(() => _viewModel?.DisposeAsync() ?? ValueTask.CompletedTask);
        await TryAsync(() => _settingsService?.DisposeAsync() ?? ValueTask.CompletedTask);
        await TryAsync(() => _hotkeys?.DisposeAsync() ?? ValueTask.CompletedTask);
        await TryAsync(() => _clipboard?.DisposeAsync() ?? ValueTask.CompletedTask);
        await TryAsync(() => _hotEdge?.DisposeAsync() ?? ValueTask.CompletedTask);
        // Async: the container holds IAsyncDisposable-only singletons, which a sync Dispose
        // refuses to tear down.
        await TryAsync(() => _services?.DisposeAsync() ?? ValueTask.CompletedTask);
        try { _singleInstance?.Dispose(); } catch { /* quitting anyway */ }
    }

    private static async ValueTask TryAsync(Func<ValueTask> dispose)
    {
        // The per-service timeout keeps one hung native teardown from starving the rest;
        // the history save runs first and is pure managed code, so it fits comfortably.
        try { await dispose().AsTask().WaitAsync(TimeSpan.FromSeconds(1.5)); }
        catch { /* quitting anyway */ }
    }
}
