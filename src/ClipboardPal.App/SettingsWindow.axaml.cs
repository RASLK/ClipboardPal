using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClipboardPal.Core.ViewModels;

namespace ClipboardPal;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        _vm.Loc.PropertyChanged += OnLocalizationChanged;
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Tunnel so Esc / outside-click cancel capture even when a child handles the event.
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, Window_PointerPressed, RoutingStrategies.Tunnel);

        if (ExcludePickButton.Flyout is Flyout flyout)
            flyout.Opening += (_, _) => _vm.RefreshSourceAppChoices();

        Closed += async (_, _) =>
        {
            _vm.Loc.PropertyChanged -= OnLocalizationChanged;
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
            _vm.CancelHotkeyCaptureCommand.Execute(null);
            await _vm.SaveAsync();
        };
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e) =>
        RefreshSelectedComboBoxText();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // After Esc via the global hook (or a successful capture), drop focus so the
        // "waiting for keys" state cannot linger on the hotkey box.
        if (e.PropertyName == nameof(SettingsViewModel.IsCapturingHotkey) &&
            !_vm.IsCapturingHotkey &&
            HotkeyBox.IsFocused)
        {
            ClearHotkeyFocus();
        }
    }

    /// <summary>
    /// A ComboBox copies the selected item's text once, when the selection changes, so the closed
    /// box would keep the old language after a live switch. Re-selecting the same index makes it
    /// take a fresh copy (the index converter ignores the intermediate -1).
    /// </summary>
    private void RefreshSelectedComboBoxText()
    {
        foreach (var combo in this.GetVisualDescendants().OfType<ComboBox>())
        {
            var selected = combo.SelectedIndex;
            if (selected < 0) continue;
            combo.SelectedIndex = -1;
            combo.SelectedIndex = selected;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var work = Screens.Primary?.WorkingArea;
        if (work is null) return;

        var maxH = Math.Max(480, work.Value.Height - 48);
        var maxW = Math.Max(560, work.Value.Width - 48);
        if (Height > maxH) Height = maxH;
        if (Width > maxW) Width = Math.Min(Width, maxW);
        MaxHeight = maxH;
        MaxWidth = maxW;
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_vm.IsCapturingHotkey)
            return;

        _vm.CancelHotkeyCaptureCommand.Execute(null);
        ClearHotkeyFocus();
        e.Handled = true;
    }

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_vm.IsCapturingHotkey)
            return;

        if (e.Source is Visual source && IsWithin(HotkeyBox, source))
            return;

        _vm.CancelHotkeyCaptureCommand.Execute(null);
        ClearHotkeyFocus();
    }

    private void HotkeyBox_GotFocus(object? sender, GotFocusEventArgs e) =>
        _vm.BeginHotkeyCaptureCommand.Execute(null);

    private void HotkeyBox_LostFocus(object? sender, RoutedEventArgs e) =>
        _vm.CancelHotkeyCaptureCommand.Execute(null);

    private void ExcludePickDone_Click(object? sender, RoutedEventArgs e)
    {
        _vm.AddSelectedSourceAppsCommand.Execute(null);
        ExcludePickButton.Flyout?.Hide();
    }

    private void ClearHotkeyFocus()
    {
        // Prefer moving focus to the window chrome so LostFocus on the box also runs.
        Focus();
        TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
    }

    private static bool IsWithin(Visual ancestor, Visual? node)
    {
        for (var v = node; v is not null; v = v.GetVisualParent())
        {
            if (ReferenceEquals(v, ancestor))
                return true;
        }

        return false;
    }
}
