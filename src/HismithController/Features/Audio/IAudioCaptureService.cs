namespace HismithController.Audio;

public interface IAudioCaptureService : IAsyncDisposable
{
    AudioCaptureState State { get; }

    event EventHandler<AudioCaptureState>? StateChanged;
    event EventHandler<AudioFrame>? SamplesAvailable;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
