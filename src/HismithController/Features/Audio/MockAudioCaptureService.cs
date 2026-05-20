namespace HismithController.Audio;

// Deterministic synthetic audio source used when --mock-audio is active.
// Generates a 120 BPM kick-drum pulse mixed with pink noise at 44100 Hz mono.
// The signal gives the spectrum visualiser something to display and gives the
// beat detector a clean, stable input to lock onto — without requiring any
// real audio to be playing on the system.
internal sealed class MockAudioCaptureService : IAudioCaptureService
{
    private volatile AudioCaptureState _state = AudioCaptureState.Stopped;
    private CancellationTokenSource? _cts;
    private Task? _generationTask;

    private const int SampleRate = 44100;

    // 1024 samples ≈ 23 ms per callback. Chosen so that a single Task.Delay
    // sleep can cover roughly one frame duration; Windows' timer resolution is
    // ~15 ms, so a smaller frame size would spin without sleeping between frames.
    private const int FrameSize = 1024;

    // 120 BPM = 2 beats/sec → one beat every SampleRate/2 = 22050 samples.
    private const int BeatPeriodSamples = SampleRate / 2;

    // Reuse a single AudioSourceFormat instance since it never changes.
    private static readonly AudioSourceFormat MockSourceFormat =
        new("Mock", SampleRate, 1, 32);

    public AudioCaptureState State => _state;

    public event EventHandler<AudioCaptureState>? StateChanged;
    public event EventHandler<AudioFrame>? SamplesAvailable;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != AudioCaptureState.Stopped)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Run the generation loop on a thread-pool thread so it can await
        // Task.Delay for realtime pacing without blocking the calling thread.
        _generationTask = Task.Run(() => GenerateAudioAsync(_cts.Token));
        SetState(AudioCaptureState.Running);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null) return;

        cts.Cancel();

        // Await the generation task so callers can rely on the audio thread
        // being fully stopped before DisposeAsync/StopAsync returns.
        if (_generationTask is not null)
            await _generationTask.ConfigureAwait(false);

        cts.Dispose();
        _cts = null;
        _generationTask = null;
        SetState(AudioCaptureState.Stopped);
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ── Generation loop ───────────────────────────────────────────────────────

    private async Task GenerateAudioAsync(CancellationToken ct)
    {
        // Fixed seed for full reproducibility: the beat detector and visualiser
        // tests always see the same sequence of samples.
        var rng = new Random(42);

        // ── Pink noise state ────────────────────────────────────────────────
        // Paul Kellett's simplified 3-pole IIR filter approximates a -3 dB/octave
        // (1/f) power spectrum from white noise, producing perceptually "warm"
        // background texture. Coefficients are empirically tuned by Kellett;
        // the * 0.05 output scale keeps the result inside the [-1, 1] float range.
        double b0 = 0, b1 = 0, b2 = 0;

        // ── Kick state ──────────────────────────────────────────────────────
        // Pre-load to BeatPeriodSamples so the very first generated sample
        // triggers a kick immediately rather than opening with a silent period.
        int    samplesSinceBeat = BeatPeriodSamples;
        double kickPhase        = 0;
        double kickEnvelope     = 0;

        // ── Realtime pacing ─────────────────────────────────────────────────
        // Instead of sleeping a fixed duration per frame we accumulate the target
        // tick for each frame. This prevents Task.Delay resolution jitter
        // (~15 ms on Windows) from causing long-term drift in the generated tempo.
        double frameMs      = (double)FrameSize / SampleRate * 1000.0;
        var    sw           = System.Diagnostics.Stopwatch.StartNew();
        long   nextFrameTick = 0;

        while (!ct.IsCancellationRequested)
        {
            var samples = new float[FrameSize];

            for (int i = 0; i < FrameSize; i++)
            {
                // ── Kick trigger ────────────────────────────────────────────
                if (samplesSinceBeat >= BeatPeriodSamples)
                {
                    samplesSinceBeat = 0;
                    kickEnvelope     = 1.0;
                    kickPhase        = 0;
                }
                samplesSinceBeat++;

                // ── Kick sample ─────────────────────────────────────────────
                // Exponentially decaying 60 Hz sine models a simplified kick drum.
                // Decay constant 0.99971 per sample:
                //   0.99971^8820 ≈ e^(-0.000290 × 8820) ≈ e^(-2.56) ≈ 0.077
                // → amplitude reaches ~7.7 % (-22 dB) after 200 ms (8820 samples).
                float kick = 0f;
                if (kickEnvelope > 0.001)
                {
                    kick          = (float)(Math.Sin(kickPhase) * kickEnvelope);
                    kickPhase    += 2.0 * Math.PI * 60.0 / SampleRate;
                    kickEnvelope *= 0.99971;
                }

                // ── Pink noise sample ───────────────────────────────────────
                double white = rng.NextDouble() * 2.0 - 1.0;
                b0 = 0.99765 * b0 + white * 0.099046;
                b1 = 0.96300 * b1 + white * 0.296516;
                b2 = 0.57000 * b2 + white * 1.052691;
                float pink = (float)((b0 + b1 + b2 + white * 0.1848) * 0.05);

                // Kick at 60 % amplitude dominates so the beat detector sees clear
                // low-frequency onsets; noise at 10 % fills the spectrum visually.
                samples[i] = Math.Clamp(kick * 0.6f + pink * 0.1f, -1f, 1f);
            }

            SamplesAvailable?.Invoke(this,
                new AudioFrame(samples, SampleRate, MockSourceFormat, DateTimeOffset.UtcNow));

            // ── Sleep to target realtime pace ───────────────────────────────
            nextFrameTick += (long)(frameMs * TimeSpan.TicksPerMillisecond);
            long delay = nextFrameTick - sw.Elapsed.Ticks;

            // Skip the sleep if we are already at or past the target: spinning
            // through immediately is cheaper than scheduling a sub-ms wakeup.
            if (delay > TimeSpan.TicksPerMillisecond)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromTicks(delay), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private void SetState(AudioCaptureState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }
}
