using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Audio;

namespace HismithController.ViewModels;

public partial class SoundModeViewModel : ObservableObject
{
    private readonly IAudioCaptureService _audioService;
    private readonly SpectrumAnalyzer _spectrumAnalyzer;

    // True when the audio service reports Running (signal present and above the silence threshold).
    [ObservableProperty]
    private bool _hasAudio;

    // User-controlled gate for beat-driven device commands (Phase 3). Reset to false
    // on every activation so the user must explicitly press Play — avoids startling them.
    [ObservableProperty]
    private bool _isPlaying;

    // 56 logarithmic frequency bins, each clamped to [0, 1].
    // SpectrumBin is a reference type so {Binding Value} on the ScaleTransform resolves
    // as a named-property binding; this propagates PropertyChanged reliably through
    // WPF's Freezable DataContext inheritance. The collection itself never changes —
    // only Value inside each bin changes — so ItemsControl never recreates containers.
    public ObservableCollection<SpectrumBin> SpectrumBins { get; } =
        new(Enumerable.Range(0, 56).Select(_ => new SpectrumBin()));

    public SoundModeViewModel(IAudioCaptureService audioService, SpectrumAnalyzer spectrumAnalyzer)
    {
        _audioService = audioService;
        _spectrumAnalyzer = spectrumAnalyzer;

        _audioService.StateChanged += OnCaptureStateChanged;
        _spectrumAnalyzer.SpectrumUpdated += OnSpectrumUpdated;
    }

    public async Task InitializeAsync()
    {
        IsPlaying = false;
        await _audioService.StartAsync();
    }

    public async Task DeactivateAsync()
    {
        IsPlaying = false;
        await _audioService.StopAsync();
        // Reset synchronously so the idle overlay shows immediately on re-entry.
        HasAudio = false;
    }

    [RelayCommand]
    private void TogglePlaying()
    {
        IsPlaying = !IsPlaying;
    }

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
}
