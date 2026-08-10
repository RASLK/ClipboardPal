using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private readonly RegionOcrService _regionOcr;
    private readonly ILocalizationService _l10n;
    private IGlobalHotkeyService? _hotkeys;
    private nint _pasteTarget;
    private DateTime _suppressDismissUntil;

    public MainWindow(
        MainViewModel viewModel,
        AppSettings settings,
        IClipboardWatcher clipboard,
        IGlobalHotkeyService hotkeys,
        IPasteService paste,
        Action openSettings,
        RegionOcrService regionOcr,
        ILocalizationService l10n)
    {
        _viewModel = viewModel;
        _settings = settings;
        _clipboard = clipboard;
        _paste = paste;
        _openSettings = openSettings;
        _regionOcr = regionOcr;
        _l10n = l10n;
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
        ApplyHeaderLayout(isSideDock);

        HintText.Text = "ClipboardPal";
        ToolTip.SetTip(HintText,
            string.Format(_viewModel.Loc["hint.panel"], _settings.Hotkey.ModifiersLabel()));
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

    private bool? _clipsVertical;
    private bool? _headerSideDock;

    private void ApplyClipsOrientation(bool vertical)
    {
        // Replacing ItemsPanel rebuilds every card — skip when the orientation is unchanged.
        if (_clipsVertical == vertical)
            return;
        _clipsVertical = vertical;

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

    /// <summary>
    /// Top/Bottom keeps a single header row. Left/Right stacks brand/search, tabs, and
    /// actions so the narrow side panel does not clip the fixed-width search + tabs row.
    /// </summary>
    private void ApplyHeaderLayout(bool sideDock)
    {
        if (_headerSideDock == sideDock)
        {
            // Still refresh search sizing when only panel width changed.
            ApplySearchSizing(sideDock);
            return;
        }

        _headerSideDock = sideDock;
        HeaderGrid.ColumnDefinitions.Clear();
        HeaderGrid.RowDefinitions.Clear();

        if (sideDock)
        {
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            HeaderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            HeaderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            HeaderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            Grid.SetRow(HeaderBrandSearch, 0);
            Grid.SetColumn(HeaderBrandSearch, 0);
            Grid.SetColumnSpan(HeaderBrandSearch, 1);

            Grid.SetRow(SpaceSwitcher, 1);
            Grid.SetColumn(SpaceSwitcher, 0);
            Grid.SetColumnSpan(SpaceSwitcher, 1);

            Grid.SetRow(HeaderActions, 2);
            Grid.SetColumn(HeaderActions, 0);
            Grid.SetColumnSpan(HeaderActions, 1);

            HeaderBrandSearch.Orientation = Orientation.Vertical;
            HeaderBrandSearch.Spacing = 10;
            HeaderBrandSearch.HorizontalAlignment = HorizontalAlignment.Stretch;
            HeaderBrandSearch.Margin = new Thickness(0, 0, 0, 8);

            SpaceSwitcher.HorizontalAlignment = HorizontalAlignment.Stretch;
            SpaceSwitcher.Margin = new Thickness(0, 0, 0, 8);
            SpaceSwitcher.Classes.Add("side-compact");

            HeaderActions.HorizontalAlignment = HorizontalAlignment.Right;
            HeaderActions.Margin = new Thickness(0);
        }
        else
        {
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            Grid.SetRow(HeaderBrandSearch, 0);
            Grid.SetColumn(HeaderBrandSearch, 0);
            Grid.SetColumnSpan(HeaderBrandSearch, 1);

            Grid.SetRow(SpaceSwitcher, 0);
            Grid.SetColumn(SpaceSwitcher, 1);
            Grid.SetColumnSpan(SpaceSwitcher, 1);

            Grid.SetRow(HeaderActions, 0);
            Grid.SetColumn(HeaderActions, 2);
            Grid.SetColumnSpan(HeaderActions, 1);

            HeaderBrandSearch.Orientation = Orientation.Horizontal;
            HeaderBrandSearch.Spacing = 16;
            HeaderBrandSearch.HorizontalAlignment = HorizontalAlignment.Left;
            HeaderBrandSearch.Margin = new Thickness(0);

            SpaceSwitcher.HorizontalAlignment = HorizontalAlignment.Center;
            SpaceSwitcher.Margin = new Thickness(12, 0);
            SpaceSwitcher.Classes.Remove("side-compact");

            HeaderActions.HorizontalAlignment = HorizontalAlignment.Right;
            HeaderActions.Margin = new Thickness(0);
        }

        ApplySearchSizing(sideDock);
    }

    private void ApplySearchSizing(bool sideDock)
    {
        if (sideDock)
        {
            // Stretch within the stacked header instead of a fixed 440px that overflows.
            SearchBorder.Width = double.NaN;
            SearchBorder.MinWidth = 0;
            SearchBorder.MaxWidth = double.PositiveInfinity;
            SearchBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            SearchBorder.Width = 440;
            SearchBorder.MinWidth = 0;
            SearchBorder.MaxWidth = double.PositiveInfinity;
            SearchBorder.HorizontalAlignment = HorizontalAlignment.Left;
        }
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
        await ApplyClipActionAsync(item, ClipAction.Paste);

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
        // Quick slots and quick mode can reach a trashed clip when the panel was left on the
        // trash tab — activation there restores instead of pasting deleted content.
        if (item.IsInTrash)
        {
            _viewModel.RestoreCommand.Execute(item);
            return true;
        }

        switch (action)
        {
            case ClipAction.Copy:
            case ClipAction.CopyAsPlainText:
                _viewModel.MarkUsed(item);
                if (!await PutOnClipboardAsync(item, action))
                    return false;
                if (_settings.HidePopupAfterCopy)
                    HidePanel();
                return true;

            case ClipAction.Paste:
            case ClipAction.PasteAsPlainText:
                _viewModel.MarkUsed(item);
                if (!await PutOnClipboardAsync(item, action))
                    return false;
                // Pasting while looking at the queue drains it: the clip goes back to
                // being history-only. Pasting the same clip from history leaves it staged.
                if (_viewModel.Space == PanelSpace.Queue && _settings.QueueRemoveAfterPaste)
                    _viewModel.ConsumeFromQueue(item);
                if (_settings.PermissionAccessibility)
                    await _paste.PasteToAsync(_pasteTarget);
                if (_settings.HidePopupAfterPaste)
                    HidePanel();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Puts the clip on the clipboard. An image clip goes over as the image itself; its OCR text is
    /// only what the "as plain text" actions ask for, and serves as a fallback if the file is gone.
    /// </summary>
    private async Task<bool> PutOnClipboardAsync(ClipItem item, ClipAction action)
    {
        var text = _viewModel.TextForAction(item, action);
        var wantsText = item.Type == ClipItemType.Text ||
                        action is ClipAction.CopyAsPlainText or ClipAction.PasteAsPlainText;

        if (wantsText && !string.IsNullOrEmpty(text))
        {
            await _clipboard.SetTextAsync(text);
            return true;
        }

        if (await TrySetClipboardAsync(item))
            return true;

        if (string.IsNullOrEmpty(text))
            return false;

        await _clipboard.SetTextAsync(text);
        return true;
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

    private int _pressClickCount;
    private ClipItem? _lastActivated;

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        _pressClickCount = e.ClickCount;

    private void Card_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (sender is Control control)
        {
            var item = control.Tag as ClipItem ?? control.DataContext as ClipItem;
            if (item is null || item.IsEditing) return;
            e.Handled = true;

            if (_viewModel.Space == PanelSpace.Trash)
            {
                _viewModel.RestoreCommand.Execute(item);
                return;
            }

            // Second release of a double click: the first one already pasted. Run the
            // configured extra action once — and only when the cursor is still over the same
            // clip, because consuming from the queue shifts the neighbouring card under it.
            if (_pressClickCount >= 2)
            {
                if (_settings.DoubleClickEnabled &&
                    _settings.DoubleClickAction != ClipAction.Paste &&
                    ReferenceEquals(item, _lastActivated))
                {
                    _ = ApplyClipActionAsync(item, _settings.DoubleClickAction);
                }
                return;
            }

            _lastActivated = item;
            _viewModel.RequestPaste(item);
        }
    }

    private void Pin_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: ClipItem item })
            _viewModel.TogglePinCommand.Execute(item);
        e.Handled = true;
    }

    /// <summary>
    /// Opens the region picker over the card's picture and recognizes whatever was framed.
    /// Pressing it again simply picks a new region, replacing the previous result.
    /// </summary>
    private async void Ocr_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control { Tag: ClipItem item })
            return;
        if (item.ImagePath is not { } path || !File.Exists(path))
            return;

        Bitmap bitmap;
        try
        {
            await using var file = File.OpenRead(path);
            bitmap = new Bitmap(file);
        }
        catch
        {
            return;
        }

        // The picker takes over the screen, so the card panel steps out of the way. It is a
        // standalone window rather than a dialog precisely because its owner is now hidden.
        HidePanel();

        using (bitmap)
        {
            var picker = new RegionPickerWindow(bitmap, _l10n);
            var closed = new TaskCompletionSource();
            picker.Closed += (_, _) => closed.TrySetResult();
            picker.Show();
            picker.Activate();
            await closed.Task;

            if (picker.Result is not { Width: > 0, Height: > 0 } region)
                return;

            await _regionOcr.RecognizeAsync(item, CropToPng(bitmap, region));
        }
    }

    /// <summary>Cuts the chosen rectangle out of the source image and encodes it as PNG.</summary>
    private static byte[] CropToPng(Bitmap source, PixelRect region)
    {
        var target = new RenderTargetBitmap(new PixelSize(region.Width, region.Height), new Vector(96, 96));
        using (var ctx = target.CreateDrawingContext())
        {
            ctx.DrawImage(
                source,
                new Rect(region.X, region.Y, region.Width, region.Height),
                new Rect(0, 0, region.Width, region.Height));
        }

        using var ms = new MemoryStream();
        target.Save(ms);
        target.Dispose();
        return ms.ToArray();
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
            _viewModel.ToggleQueueCommand.Execute(item);
        e.Handled = true;
    }

    private void TitleEditor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: ClipItem item })
            return;

        if (e.Key == Key.Enter)
        {
            item.IsEditing = false;
            SearchBox.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            item.CancelEdit();
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
