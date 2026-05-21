using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using HismithController.ViewModels;

namespace HismithController.Views;

public partial class ManualModeView : UserControl
{
    private ManualModeViewModel? _subscribedVm;

    public ManualModeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => UnsubscribeBeatPulse();
        HookBeatPulse(DataContext as ManualModeViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => HookBeatPulse(e.NewValue as ManualModeViewModel);

    private void HookBeatPulse(ManualModeViewModel? vm)
    {
        UnsubscribeBeatPulse();
        if (vm is null) return;
        _subscribedVm = vm;
        vm.BeatPulse += OnBeatPulse;
    }

    private void UnsubscribeBeatPulse()
    {
        if (_subscribedVm is null) return;
        _subscribedVm.BeatPulse -= OnBeatPulse;
        _subscribedVm = null;
    }

    private void OnBeatPulse()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnBeatPulse);
            return;
        }
        RunRingPulse(PercentBeatRing);
        RunRingPulse(BpmBeatRing);
    }

    private static void RunRingPulse(Border ring)
    {
        var anim = new DoubleAnimation
        {
            From = 0.85,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ring.BeginAnimation(UIElement.OpacityProperty, anim);
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

    private void OnPercentFieldPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Move focus to the Window rather than clearing it entirely; Keyboard.ClearFocus()
            // severs WPF's key-event routing, which silences the spacebar emergency stop.
            SpeedSlider.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            // Window.PreviewKeyDown handles space when no text field is focused; mirror it here
            // so the emergency stop also fires while this field holds keyboard focus.
            e.Handled = true;
            TriggerEmergencyStop();
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

    private void OnBpmFieldPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SpeedSlider.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            e.Handled = true;
            TriggerEmergencyStop();
        }
    }

    private void TriggerEmergencyStop()
    {
        if (Window.GetWindow(this)?.DataContext is not MainViewModel vm) return;
        if (vm.EmergencyStopCommand.CanExecute(null))
            vm.EmergencyStopCommand.Execute(null);
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
