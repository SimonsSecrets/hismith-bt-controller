using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Audio;
using HismithController.BeatDetection;

namespace HismithController.ViewModels;

public partial class SoundModeViewModel : ObservableObject
{
    private readonly IAudioCaptureService _audioService;
    private readonly SpectrumAnalyzer     _spectrumAnalyzer;
    private readonly IBeatDetector        _beatDetector;

    // Resets BeatTick to false ~120 ms after each beat so DataTriggers that
    // bind to BeatTick can observe the false→true transition on the next beat.
    // 120 ms is well below the min inter-beat gap at the 300 BPM detection cap
    // (200 ms), guaranteeing at least 80 ms of "false" time between beats.
    private readonly DispatcherTimer _beatTickTimer;

    // Fired on the UI thread on each beat while IsPlaying, for the code-behind
    // to trigger imperative animations (ring scale + opacity) that cannot be
    // driven cleanly by DataTrigger alone when beats arrive rapidly.
    public event Action? BeatPulse;

    // True when the audio service reports Running and signal is above silence threshold.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLiveStats))]
    private bool _hasAudio;

    // User-controlled gate: reset to false on every tab activation so the user
    // must press Play explicitly — avoids startling them with device activity.
    // Pausing does NOT stop capture or beat detection — the visualizer keeps moving.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLiveStats))]
    private bool _isPlaying;

    // Current music BPM from the beat detector; updated on every detected beat
    // regardless of IsPlaying so the readout is current the moment Play is pressed.
    [ObservableProperty]
    private int _liveBpm;

    // Flips true on each beat when IsPlaying; reset to false after ~120 ms by
    // _beatTickTimer. Drives the live-dot DataTrigger in the view and keeps the
    // bool cycling (false→true) so DataTriggers always see the edge.
    [ObservableProperty]
    private bool _beatTick;

    // True when the live stats bar should show real values rather than dashes.
    // HasAudio ensures at least one beat has been heard; IsPlaying ensures the
    // user has opted in to beat-driven behaviour.
    public bool HasLiveStats => HasAudio && IsPlaying;

    // 56 logarithmic frequency bins, each clamped to [0, 1].
    // SpectrumBin is a reference type so {Binding Value} on the bar Border
    // resolves as a named-property binding; this propagates PropertyChanged
    // reliably through WPF's Freezable DataContext inheritance. The collection
    // itself never changes — only Value inside each bin changes — so ItemsControl
    // never recreates containers.
    public ObservableCollection<SpectrumBin> SpectrumBins { get; } =
        new(Enumerable.Range(0, 56).Select(_ => new SpectrumBin()));

    public SoundModeViewModel(
        IAudioCaptureService audioService,
        SpectrumAnalyzer     spectrumAnalyzer,
        IBeatDetector        beatDetector)
    {
        _audioService     = audioService;
        _spectrumAnalyzer = spectrumAnalyzer;
        _beatDetector     = beatDetector;

        _audioService.StateChanged         += OnCaptureStateChanged;
        _spectrumAnalyzer.SpectrumUpdated  += OnSpectrumUpdated;
        _beatDetector.BeatDetected         += OnBeatDetected;

        _beatTickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _beatTickTimer.Tick += OnBeatTickTimerTick;
    }

    public async Task InitializeAsync()
    {
        IsPlaying = false;
        await _audioService.StartAsync();
    }

    public async Task DeactivateAsync()
    {
        IsPlaying = false;   // triggers OnIsPlayingChanged → stops timer + clears BeatTick
        await _audioService.StopAsync();
        // Reset HasAudio synchronously so the idle overlay appears immediately on re-entry.
        HasAudio = false;
    }

    [RelayCommand]
    private void TogglePlaying()
    {
        IsPlaying = !IsPlaying;
    }

    // ── Property-change side effects ───────────────────────────────────────────

    partial void OnIsPlayingChanged(bool value)
    {
        if (!value)
        {
            // Stop the timer and clear the tick immediately so no residual
            // flashes appear after the user pauses or the tab is deactivated.
            _beatTickTimer.Stop();
            BeatTick = false;
        }
    }

    // ── Audio thread callbacks ─────────────────────────────────────────────────

    // Runs on the NAudio/Task.Run audio thread; marshals HasAudio update to the UI thread.
    private void OnCaptureStateChanged(object? sender, AudioCaptureState state)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            HasAudio = state == AudioCaptureState.Running;
        });
    }

    // Runs on the audio thread at ~30 fps; marshals 56 Value updates to the UI thread.
    // Writing SpectrumBin.Value fires PropertyChanged on the existing objects —
    // no CollectionChanged, so ItemsControl containers are never recreated.
    private void OnSpectrumUpdated(object? sender, double[] bins)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < bins.Length; i++)
                SpectrumBins[i].Value = Math.Clamp(bins[i], 0.0, 1.0);
        });
    }

    // Runs on the NAudio audio thread; marshals all beat-related UI updates to
    // the UI dispatcher so DispatcherTimer and ObservableProperty writes are safe.
    private void OnBeatDetected(object? sender, BeatEventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Always track the current BPM so the readout is up-to-date the
            // instant the user presses Play, even if they were watching paused.
            LiveBpm = _beatDetector.CurrentBpm;

            if (!IsPlaying) return;

            // BeatTick: force a false→true edge even if the previous tick never
            // had time to reset (rapid beats just after unpausing). We stop the
            // timer first so OnBeatTickTimerTick cannot fire between the two sets.
            _beatTickTimer.Stop();
            BeatTick = false;   // ensure the DataTrigger sees the rising edge
            BeatTick = true;
            _beatTickTimer.Start();

            // Separate event for imperative animations (ring pulse) that need to
            // restart cleanly at any beat frequency without DataTrigger cycling.
            BeatPulse?.Invoke();
        });
    }

    // ── UI thread ─────────────────────────────────────────────────────────────

    private void OnBeatTickTimerTick(object? sender, EventArgs e)
    {
        BeatTick = false;
        _beatTickTimer.Stop();
    }
}
