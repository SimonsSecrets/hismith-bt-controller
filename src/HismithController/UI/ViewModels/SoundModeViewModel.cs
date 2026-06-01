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

    // Bridges the silent gaps between clicks of a sparse source (slow metronome).
    // The capture service reports NoSignal during these gaps (RMS dips), but a
    // recent beat means music IS playing. Holds HasAudio=true for one slow-tempo
    // beat period; if no further beat arrives, falls back to the live RMS state.
    // 4000 ms ≈ 15 BPM, matching the detector's slowest accepted tempo (MaxIbiMs).
    private const int AudioHoldMs = 4000;
    private readonly DispatcherTimer _audioHoldTimer;

    // Drives the visual beat pulse (ring + live dot) from the detected BPM rather
    // than from raw onset events. Onsets fire on every kick/snare transient, which
    // diverges from the autocorrelation tempo shown in the readout; pacing the pulse
    // off LiveBpm keeps the indicator and the number on the same source. Interval is
    // the beat period (60000/BPM ms), recomputed whenever LiveBpm changes.
    private readonly DispatcherTimer _pulseTimer;

    // BPM the _pulseTimer interval is currently set for. Lets UpdatePulseTimer skip
    // restarting (which would reset the pulse phase) when LiveBpm re-reports the same
    // value, while still re-syncing on an actual tempo change.
    private int _pulseTimerBpm;

    // Fired on the UI thread on each beat while driving the device, for the
    // code-behind to trigger imperative animations (ring scale + opacity) that
    // cannot be driven cleanly by DataTrigger alone when beats arrive rapidly.
    public event Action? BeatPulse;

    // True when the audio service reports Running and signal is above silence threshold.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivelyDriving))]
    private bool _hasAudio;

    // User-controlled gate for sending detected beats to the device. Reset to
    // false on every tab activation so the user must opt in explicitly — avoids
    // startling them with device activity. Disabling it does NOT stop capture or
    // beat detection: the visualizer and BPM readout keep updating regardless.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActivelyDriving))]
    private bool _isDrivingDevice;

    // Current music BPM from the beat detector; updated on every detected beat
    // regardless of IsDrivingDevice so the readout is always current.
    [ObservableProperty]
    private int _liveBpm;

    // Flips true on each beat while actively driving; reset to false after ~120 ms
    // by _beatTickTimer. Drives the live-dot DataTrigger in the view and keeps the
    // bool cycling (false→true) so DataTriggers always see the edge.
    [ObservableProperty]
    private bool _beatTick;

    // True when the device is actually being driven from live audio: there is a
    // signal AND the user has enabled device driving. Gates the device-side stats
    // (Device/Speed) and the BPM-paced beat pulse.
    public bool IsActivelyDriving => HasAudio && IsDrivingDevice;

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

        _audioHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AudioHoldMs) };
        _audioHoldTimer.Tick += OnAudioHoldTimerTick;

        _pulseTimer = new DispatcherTimer();
        _pulseTimer.Tick += OnPulseTimerTick;
    }

    public async Task InitializeAsync()
    {
        IsDrivingDevice = false;
        await _audioService.StartAsync();
    }

    public async Task DeactivateAsync()
    {
        IsDrivingDevice = false;   // triggers OnIsDrivingDeviceChanged → stops timer + clears BeatTick
        await _audioService.StopAsync();
        // Stop the hold timer so it cannot fire a HasAudio change after tab exit.
        _audioHoldTimer.Stop();
        // Reset HasAudio synchronously so the idle overlay appears immediately on re-entry.
        HasAudio = false;
    }

    [RelayCommand]
    private void ToggleDrivingDevice()
    {
        IsDrivingDevice = !IsDrivingDevice;
    }

    // Called by the global emergency stop. Disables device driving so no further
    // BPM is sent until the user opts back in; the bound play/pause button flips
    // back to its "off" state and OnIsDrivingDeviceChanged stops the pulse + tick.
    // Capture and beat detection keep running (same as a normal toggle-off).
    public void ForceStop() => IsDrivingDevice = false;

    // ── Property-change side effects ───────────────────────────────────────────

    partial void OnIsDrivingDeviceChanged(bool value)
    {
        if (!value)
        {
            // Stop the timer and clear the tick immediately so no residual
            // flashes appear after the user pauses or the tab is deactivated.
            _beatTickTimer.Stop();
            BeatTick = false;
        }

        UpdatePulseTimer();
    }

    // LiveBpm changed → re-pace the pulse so the ring/dot keep tracking the readout.
    partial void OnLiveBpmChanged(int value) => UpdatePulseTimer();

    // Audio came or went → start/stop the pulse alongside the driving gate.
    partial void OnHasAudioChanged(bool value) => UpdatePulseTimer();

    // (Re)starts or stops the BPM-driven pulse timer to match the current state.
    // Pulses only while actively driving (device-driving enabled + audio) and a
    // tempo is known; restarts only on an actual BPM change to avoid continually
    // resetting the pulse phase as LiveBpm re-reports the same value every beat.
    private void UpdatePulseTimer()
    {
        if (IsActivelyDriving && LiveBpm > 0)
        {
            if (_pulseTimerBpm == LiveBpm && _pulseTimer.IsEnabled) return;

            _pulseTimerBpm       = LiveBpm;
            _pulseTimer.Interval = TimeSpan.FromMilliseconds(60000.0 / LiveBpm);
            _pulseTimer.Stop();   // restart so the new interval takes effect immediately
            _pulseTimer.Start();
        }
        else
        {
            _pulseTimer.Stop();
            _pulseTimerBpm = 0;
        }
    }

    // ── Audio thread callbacks ─────────────────────────────────────────────────

    // Runs on the NAudio/Task.Run audio thread; marshals HasAudio update to the UI thread.
    private void OnCaptureStateChanged(object? sender, AudioCaptureState state)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (state == AudioCaptureState.Running)
                HasAudio = true;
            else if (!_audioHoldTimer.IsEnabled)
                HasAudio = false;   // no recent beat holding us up → reflect reality
            // else: a recent beat is holding HasAudio=true; ignore the transient NoSignal.
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

            // A detected beat means music is playing even if the instant is silent
            // (sparse/slow source). Keep the overlay hidden and rearm the hold window.
            // The visual pulse itself is NOT fired here — it is paced by _pulseTimer
            // off LiveBpm so the ring/dot match the detected BPM rather than the (often
            // denser and jittery) raw onset rate.
            HasAudio = true;
            _audioHoldTimer.Stop();
            _audioHoldTimer.Start();
        });
    }

    // ── UI thread ─────────────────────────────────────────────────────────────

    // Fires one visual pulse per beat period. Drives both the BeatTick DataTrigger
    // (live dot) and the BeatPulse animation (ring) from the same BPM-paced source.
    private void OnPulseTimerTick(object? sender, EventArgs e)
    {
        // BeatTick: force a false→true edge even if the previous tick never had time
        // to reset (high BPM). Stop the reset timer first so OnBeatTickTimerTick cannot
        // fire between the two sets.
        _beatTickTimer.Stop();
        BeatTick = false;   // ensure the DataTrigger sees the rising edge
        BeatTick = true;
        _beatTickTimer.Start();

        // Separate event for the imperative ring animation that restarts cleanly
        // at any beat frequency without DataTrigger cycling.
        BeatPulse?.Invoke();
    }

    private void OnBeatTickTimerTick(object? sender, EventArgs e)
    {
        BeatTick = false;
        _beatTickTimer.Stop();
    }

    // Hold window expired with no new beat: fall back to the live capture state.
    private void OnAudioHoldTimerTick(object? sender, EventArgs e)
    {
        _audioHoldTimer.Stop();
        HasAudio = _audioService.State == AudioCaptureState.Running;
    }
}
