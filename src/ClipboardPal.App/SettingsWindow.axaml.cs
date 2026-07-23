using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

        Closed += async (_, _) =>
        {
            _vm.CancelHotkeyCaptureCommand.Execute(null);
            await _vm.SaveAsync();
        };
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
