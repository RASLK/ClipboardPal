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

        Closed += async (_, _) =>
        {
            _vm.Loc.PropertyChanged -= OnLocalizationChanged;
            _vm.CancelHotkeyCaptureCommand.Execute(null);
            await _vm.SaveAsync();
        };
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e) =>
        RefreshSelectedComboBoxText();

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

    private void HotkeyBox_GotFocus(object? sender, GotFocusEventArgs e) =>
        _vm.BeginHotkeyCaptureCommand.Execute(null);

    private void HotkeyBox_LostFocus(object? sender, RoutedEventArgs e) =>
        _vm.CancelHotkeyCaptureCommand.Execute(null);
}
