using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipboardPal.Core.ViewModels;

/// <summary>A checkable entry in the "pick from clipboard history" exclusions picker.</summary>
public sealed partial class SelectableSourceApp(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;
}
