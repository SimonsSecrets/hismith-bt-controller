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

    private void OnSliderDragStarted(object sender, DragStartedEventArgs e)
    {
        if (DataContext is ManualModeViewModel vm)
            vm.NotifyDragging(true);
    }

    private void OnSliderDragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is ManualModeViewModel vm)
            vm.NotifyDragging(false);
    }

    private void OnPercentFieldGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is ManualModeViewModel vm)
            vm.NotifyPercentFieldFocus(true);
        if (sender is TextBox box)
            box.SelectAll();
    }

    private void OnPercentFieldLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box || DataContext is not ManualModeViewModel vm)
            return;
        if (int.TryParse(box.Text, out int pct))
            vm.SetTargetFromPercent(pct);
        vm.NotifyPercentFieldFocus(false);
    }

    private void OnPercentFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnBpmFieldGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is ManualModeViewModel vm)
            vm.NotifyBpmFieldFocus(true);
        if (sender is TextBox box)
            box.SelectAll();
    }

    private void OnBpmFieldLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox box || DataContext is not ManualModeViewModel vm)
            return;
        if (int.TryParse(box.Text, out int bpm))
            vm.SetTargetBpm(bpm);
        vm.NotifyBpmFieldFocus(false);
    }

    private void OnBpmFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnNumFieldPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox box && !box.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            box.Focus();
        }
    }
}
