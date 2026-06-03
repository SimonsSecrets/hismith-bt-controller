using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using HismithController.ViewModels;

namespace HismithController.Views;

public partial class SoundModeView : UserControl
{
    private SoundModeViewModel? _subscribedVm;

    public SoundModeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => UnsubscribeBeatPulse();
        HookBeatPulse(DataContext as SoundModeViewModel);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        => HookBeatPulse(e.NewValue as SoundModeViewModel);

    private void HookBeatPulse(SoundModeViewModel? vm)
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

    // BeatPulse fires on the UI thread (dispatched in the ViewModel).
    // BeginAnimation restarts cleanly on each call, so rapid beats at
    // high BPM never miss a pulse or leave stale animated values behind.
    private void OnBeatPulse()
    {
        // Full-bleed border flash (design beatRingPulse: opacity 0.85 → 0 over 360 ms,
        // no scale — the border is fixed at the visualizer's edge).
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(360));

        BeatRingElement.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.85, 0, duration) { EasingFunction = easing });
    }
}
