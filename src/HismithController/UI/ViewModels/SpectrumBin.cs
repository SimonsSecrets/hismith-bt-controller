using CommunityToolkit.Mvvm.ComponentModel;

namespace HismithController.ViewModels;

// Wraps a single spectrum magnitude value as an observable reference type.
// Using a reference type (instead of raw double) lets WPF resolve {Binding Value}
// as a stable named-property binding — the reliable path for Freezable DataContext
// inheritance. Binding to the DataContext itself ({Binding} on a double value type)
// does not reliably propagate Replace-action CollectionChanged updates through
// ScaleTransform.ScaleY.
public partial class SpectrumBin : ObservableObject
{
    [ObservableProperty]
    private double _value;
}
