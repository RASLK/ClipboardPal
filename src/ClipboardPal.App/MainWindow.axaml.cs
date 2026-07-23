using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Models;
using ClipboardPal.Core.Services;
using ClipboardPal.Core.ViewModels;

namespace ClipboardPal;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private readonly IClipboardWatcher _clipboard;
    private readonly IPasteService _paste;
    private readonly Action _openSettings;
    private IGlobalHotkeyService? _hotkeys;
    private nint _pasteTarget;
    private DateTime _suppressDismissUntil;

    public MainWindow(
        MainViewModel viewModel,
        AppSettings settings,
        IClipboardWatcher clipboard,
        IGlobalHotkeyService hotkeys,
        IPasteService paste,
        Action openSettings)
    {
        _viewModel = viewModel;
        _settings = settings;
        _clipboard = clipboard;
        _paste = paste;
        _openSettings = openSettings;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PasteRequested += OnPasteRequested;
        Opacity = 0;
        ShowActivated = false;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Opacity < 0.1)
            Position = new PixelPoint(-10000, -10000);
    }

    public void AttachHotkeys(IGlobalHotkeyService hotkeys)
    {
        _hotkeys = hotkeys;
        hotkeys.QuickSlotPressed += OnQuickSlot;
        hotkeys.QuickChordChanged += active =>
            Dispatcher.UIThread.Post(() => _viewModel.SetQuickSelectActive(active));
        hotkeys.MousePressed += OnGlobalMousePressed;
    }

    public void TogglePanel()
    {
        if (Opacity > 0.5 && IsVisible && Position.X >= 0)
            HidePanel();
        else
            ShowPanel();
    }

    public void ShowPanel()
    {
        _pasteTarget = _paste.CaptureForegroundWindow();
        Topmost = _settings.KeepWindowOnTop;

        var screen = Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 800);
        var height = (int)_settings.PanelHeight;
        var sideWidth = Math.Clamp(Math.Min(440, screen.Width / 3), 320, 520);
        var isSideDock = _settings.PanelDock is PanelDock.Left or PanelDock.Right;

        switch (_settings.PanelDock)
        {
            case PanelDock.Bottom:
                Width = screen.Width;
                Height = height;
                Position = new PixelPoint(screen.X, screen.Y + screen.Height - height);
                PanelBorder.CornerRadius = new CornerRadius(16, 16, 0, 0);
                PanelBorder.BorderThickness = new Thickness(1, 1, 1, 0);
                break;
            case PanelDock.Top:
                Width = screen.Width;
                Height = height;
                Position = new PixelPoint(screen.X, screen.Y);
                PanelBorder.CornerRadius = new CornerRadius(0, 0, 16, 16);
                PanelBorder.BorderThickness = new Thickness(1, 0, 1, 1);
                break;
            case PanelDock.Left:
                Width = sideWidth;
                Height = screen.Height;
                Position = new PixelPoint(screen.X, screen.Y);
                PanelBorder.CornerRadius = new CornerRadius(0, 16, 16, 0);
                PanelBorder.BorderThickness = new Thickness(0, 1, 1, 1);
                break;
            case PanelDock.Right:
                Width = sideWidth;
                Height = screen.Height;
                Position = new PixelPoint(screen.X + screen.Width - (int)sideWidth, screen.Y);
                PanelBorder.CornerRadius = new CornerRadius(16, 0, 0, 16);
                PanelBorder.BorderThickness = new Thickness(1, 1, 0, 1);
                break;
        }

        ApplyClipsOrientation(isSideDock);

        // Keep search compact; shrink on side dock so it doesn't overflow.
        SearchBorder.Width = isSideDock
            ? Math.Clamp(sideWidth - 48, 180, 280)
            : 440;

        HintText.Text = "ClipboardPal";
        ToolTip.SetTip(HintText,
            $"Enter — вставить · {_settings.Hotkey.ModifiersLabel()} + цифра — быстрый слот · Esc — закрыть");
        _viewModel.SearchText = string.Empty;
        _viewModel.RefreshFilter();

        // Prevent tray-click / focus races from instantly dismissing the panel.
        _suppressDismissUntil = DateTime.UtcNow.AddMilliseconds(600);

        Opacity = 1;
        Show();
        Activate();

        if (_settings.SelectLastOnOpen)
            _viewModel.SelectLast();

        SearchBox.Focus();

        if (_hotkeys is not null)
        {
            _hotkeys.PanelVisible = true;
            _hotkeys.RecheckChord();
        }
    }

    private void ApplyClipsOrientation(bool vertical)
    {
        ClipsScroll.HorizontalScrollBarVisibility = vertical
            ? ScrollBarVisibility.Disabled
            : ScrollBarVisibility.Auto;
        ClipsScroll.VerticalScrollBarVisibility = vertical
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Disabled;

        ClipsList.ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel
        {
            Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(2, 6, 2, 2)
        });
    }

    public void HidePanel()
    {
        if (_hotkeys is not null)
            _hotkeys.PanelVisible = false;
        _viewModel.SetQuickSelectActive(false);
        _viewModel.QuickModeReset();
        Opacity = 0;
        Position = new PixelPoint(-10000, -10000);
    }

    public void CommitClip(ClipItem item) =>
        _ = ApplyClipActionAsync(item, _settings.QuickSlotAction);

    private async void OnPasteRequested(ClipItem item) =>
        await ApplyClipActionAsync(item, ResolveClickAction());

    private ClipAction ResolveClickAction() =>
        _settings.DoubleClickEnabled ? _settings.DoubleClickAction : ClipAction.Paste;

    private void OnQuickSlot(int slot)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_viewModel.ItemFromSlot(slot) is not { } item)
                return;
            await ApplyClipActionAsync(item, _settings.QuickSlotAction);
        });
    }

    private async Task<bool> ApplyClipActionAsync(ClipItem item, ClipAction action)
    {
        // Queue: for paste actions, optionally consume from queue first.
        if (_settings.QueueEnabled &&
            _viewModel.Queue.Items.Count > 0 &&
            action is ClipAction.Paste or ClipAction.PasteAsPlainText)
        {
            var queued = _viewModel.Queue.TakeNext(_settings.QueuePasteMode);
            if (queued is not null)
                item = queued;
        }

        var text = _viewModel.TextForAction(item, action);

        switch (action)
        {
            case ClipAction.Copy:
            case ClipAction.CopyAsPlainText:
                _viewModel.MarkUsed(item);
                if (item.Type == ClipItemType.Text || !string.IsNullOrEmpty(text))
                    await _clipboard.SetTextAsync(text ?? string.Empty);
                else if (!await TrySetClipboardAsync(item))
                    return false;
                if (_settings.HidePopupAfterCopy)
                    HidePanel();
                return true;

            case ClipAction.Paste:
            case ClipAction.PasteAsPlainText:
                _viewModel.MarkUsed(item);
                if (item.Type == ClipItemType.Text || !string.IsNullOrEmpty(text))
                    await _clipboard.SetTextAsync(text ?? string.Empty);
                else if (!await TrySetClipboardAsync(item))
                    return false;
                if (_settings.PermissionAccessibility)
                    await _paste.PasteToAsync(_pasteTarget);
                if (_settings.HidePopupAfterPaste)
                    HidePanel();
                return true;

            default:
                return false;
        }
    }

    private async Task<bool> TrySetClipboardAsync(ClipItem item)
    {
        try
        {
            if (item.Type == ClipItemType.Text)
            {
                await _clipboard.SetTextAsync(item.Text ?? string.Empty);
                return true;
            }

            if (item.ImagePath is not null && File.Exists(item.ImagePath))
            {
                var bytes = await File.ReadAllBytesAsync(item.ImagePath);
                await _clipboard.SetImageAsync(bytes);
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow < _suppressDismissUntil)
            return;
        if (_settings.HidePopupOnOutsideClick)
            HidePanel();
    }

    private void OnGlobalMousePressed(int screenX, int screenY)
    {
        // SharpHook raises this on a thread-pool thread — never touch Avalonia properties here.
        if (!_settings.HidePopupOnOutsideClick)
            return;
        if (DateTime.UtcNow < _suppressDismissUntil)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_settings.HidePopupOnOutsideClick)
                return;
            if (DateTime.UtcNow < _suppressDismissUntil)
                return;
            if (Opacity < 0.5 || Position.X < -1000)
                return;
            if (!IsScreenPointInsidePanel(screenX, screenY))
                HidePanel();
        });
    }

    private bool IsScreenPointInsidePanel(int screenX, int screenY)
    {
        var scale = Math.Max(RenderScaling, 0.5);
        var w = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var h = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        // Small inset so clicks on the outer edge of the transparent window still count as outside.
        var rect = new PixelRect(Position.X, Position.Y, w, h);
        return rect.Contains(new PixelPoint(screenX, screenY));
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HidePanel();
            e.Handled = true;
        }
    }

    private void Settings_Click(object? sender, RoutedEventArgs e)
    {
        HidePanel();
        _openSettings();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var item = _viewModel.SelectedOrFirst();
            if (item is not null)
                _viewModel.RequestPaste(item);
            e.Handled = true;
        }
    }

    private void Card_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (sender is Control control)
        {
            var item = control.Tag as ClipItem ?? control.DataContext as ClipItem;
            if (item is null || item.IsEditing) return;

            if (_viewModel.ShowTrash)
            {
                _viewModel.RestoreCommand.Execute(item);
                e.Handled = true;
                return;
            }

            _viewModel.RequestPaste(item);
            e.Handled = true;
        }
    }

    private void Pin_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: ClipItem item })
            _viewModel.TogglePinCommand.Execute(item);
        e.Handled = true;
    }

    private void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: ClipItem item })
            _viewModel.RenameCommand.Execute(item);
        e.Handled = true;
    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: ClipItem item })
            _viewModel.DeleteCommand.Execute(item);
        e.Handled = true;
    }

    private void Queue_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: ClipItem item })
            _viewModel.AddToQueueCommand.Execute(item);
        e.Handled = true;
    }

    private void TitleEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape && sender is TextBox { DataContext: ClipItem item })
        {
            item.IsEditing = false;
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void TitleEditor_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ClipItem item })
            item.IsEditing = false;
    }
}
