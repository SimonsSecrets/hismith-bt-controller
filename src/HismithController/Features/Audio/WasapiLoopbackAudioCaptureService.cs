namespace HismithController.Audio;

// Stub — implementation added in step 1.2.
internal sealed class WasapiLoopbackAudioCaptureService : IAudioCaptureService
{
    public AudioCaptureState State => AudioCaptureState.Stopped;

#pragma warning disable CS0067 // raised in step 1.2 implementation
    public event EventHandler<AudioCaptureState>? StateChanged;
    public event EventHandler<AudioFrame>? SamplesAvailable;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task StopAsync() =>
        throw new NotImplementedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
