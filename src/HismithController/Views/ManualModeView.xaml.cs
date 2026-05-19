using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using HismithController.ViewModels;

namespace HismithController.Views;

public partial class ManualModeView : UserControl
{
    public ManualModeView()
    {
        InitializeComponent();
    }

    private void OnSliderDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is ManualModeViewModel vm)
            vm.CommitSliderValue();
    }

    private void OnNumFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is UIElement element)
        {
            element.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }
}
