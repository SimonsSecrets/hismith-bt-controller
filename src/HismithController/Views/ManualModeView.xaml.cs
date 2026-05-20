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
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnPercentFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box)
        {
            CommitPercent(box);
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void OnPercentFieldLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box)
            CommitPercent(box);
    }

    private void OnNumFieldGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox box)
            box.SelectAll();
    }

    private void OnNumFieldPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox box && !box.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            box.Focus();
        }
    }

    private void CommitPercent(TextBox box)
    {
        if (DataContext is not ManualModeViewModel vm)
            return;
        if (int.TryParse(box.Text, out int pct))
            vm.SetTargetFromPercent(pct);
        else
            box.Text = vm.TargetSpeedPercent.ToString();
    }
}
