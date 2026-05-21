using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(350));

        // Opacity: flash in at 0.75 and decay to 0 — ring fades out as it expands.
        BeatRingElement.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation(0.75, 0, duration) { EasingFunction = easing });

        // Scale: expand from 0.7× to 1.4× around the ring's own centre.
        // RenderTransformOrigin="0.5,0.5" on the Ellipse ensures it grows
        // outward symmetrically without translating.
        var transform = (ScaleTransform)BeatRingElement.RenderTransform;
        var scaleAnim = new DoubleAnimation(0.7, 1.4, duration) { EasingFunction = easing };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
    }
}
