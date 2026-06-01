using HismithController.Audio;
using HismithController.BeatDetection;
using HismithController.Configuration;
using Xunit.Abstractions;

namespace HismithController.Tests.BeatDetection;

// Real-time integration test driving the detector with a CLEAN metronome signal:
// short low-frequency clicks separated by near-silence. Paced in wall-clock time
// because the onset gate uses Environment.TickCount64. Reproduces the metronome
// regression where a clean click track should lock onto its true tempo.
public class MockSignalBeatDetectionTests
{
    private readonly ITestOutputHelper _out;
    public MockSignalBeatDetectionTests(ITestOutputHelper output) => _out = output;

    private const int SampleRate = 44100;
    private const int FrameSize  = 1024;

    private sealed class FakeAudioService : IAudioCaptureService
    {
        public AudioCaptureState State { get; private set; } = AudioCaptureState.Running;
        public event EventHandler<AudioCaptureState>? StateChanged;
        public event EventHandler<AudioFrame>? SamplesAvailable;
        public void Emit(AudioFrame f) => SamplesAvailable?.Invoke(this, f);
        public void RaiseState(AudioCaptureState s) { State = s; StateChanged?.Invoke(this, s); }
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static AudioFrame SilentFrame() =>
        new(new float[FrameSize], SampleRate,
            new AudioSourceFormat("Mock", SampleRate, 1, 32), DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(120)]
    [InlineData(90)]
    public void CleanMetronome_LocksOntoTrueTempo(int bpm)
    {
        var audio = new FakeAudioService();
        var detector = new SpectralFluxBeatDetector(audio, new AppSettings());

        int beatCount = 0;
        detector.BeatDetected += (_, _) => beatCount++;

        int periodSamples = SampleRate * 60 / bpm;
        int clickSamples   = (int)(0.030 * SampleRate); // 30 ms click
        int samplesSinceBeat = periodSamples; // fire immediately

        double frameMs = (double)FrameSize / SampleRate * 1000.0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long nextFrameTick = 0;

        double seconds = 8.0;
        int frames = (int)(seconds * SampleRate / FrameSize);
        for (int f = 0; f < frames; f++)
        {
            var samples = new float[FrameSize];
            for (int i = 0; i < FrameSize; i++)
            {
                if (samplesSinceBeat >= periodSamples) samplesSinceBeat = 0;
                int t = samplesSinceBeat++;

                float v;
                if (t < clickSamples)
                {
                    // 200 Hz click in the detector's 0–300 Hz band, sharp attack,
                    // ~8 ms decay → a clean isolated low-frequency transient.
                    double env = Math.Exp(-t / (0.008 * SampleRate));
                    v = (float)(Math.Sin(2.0 * Math.PI * 200.0 * t / SampleRate) * env * 0.8);
                }
                else
                {
                    // Perfect silence between clicks: flux collapses to 0, so the
                    // adaptive threshold (threshold <= 0 guard) suppresses spurious
                    // gap onsets — only the clicks fire.
                    v = 0f;
                }
                samples[i] = v;
            }

            audio.Emit(new AudioFrame(samples, SampleRate,
                new AudioSourceFormat("Mock", SampleRate, 1, 32), DateTimeOffset.UtcNow));

            nextFrameTick += (long)(frameMs * TimeSpan.TicksPerMillisecond);
            long delay = nextFrameTick - sw.Elapsed.Ticks;
            if (delay > TimeSpan.TicksPerMillisecond)
                Thread.Sleep(TimeSpan.FromTicks(delay));
        }

        int expectedBeats = (int)(seconds * bpm / 60.0);
        _out.WriteLine($"bpm={bpm} expectedBeats~{expectedBeats} beatCount={beatCount} " +
                       $"CurrentBpm={detector.CurrentBpm} Confidence={detector.Confidence}");

        Assert.InRange(beatCount, expectedBeats - 3, expectedBeats + 3);
        Assert.InRange(detector.CurrentBpm, bpm - 8, bpm + 8);
    }

    [Fact]
    public void NoSignalFrames_DoNotFirePhantomBeats()
    {
        // Reproduces the "stuck at 240 / no idle overlay" symptom: the capture service
        // keeps publishing near-silent frames in the NoSignal state, and the adaptive
        // threshold would otherwise collapse and emit phantom beats on noise.
        var audio = new FakeAudioService();
        var detector = new SpectralFluxBeatDetector(audio, new AppSettings());
        int beatCount = 0;
        detector.BeatDetected += (_, _) => beatCount++;

        audio.RaiseState(AudioCaptureState.NoSignal);
        for (int f = 0; f < 400; f++) // many frames, fed fast
            audio.Emit(SilentFrame());

        Assert.Equal(0, beatCount);
    }

    [Fact]
    public void Stop_ResetsReportedBpm()
    {
        var audio = new FakeAudioService();
        var detector = new SpectralFluxBeatDetector(audio, new AppSettings());

        // Lock onto 120 BPM, then stop the source.
        int periodSamples = SampleRate / 2; // 120 BPM
        int clickSamples = (int)(0.030 * SampleRate);
        int sinceBeat = periodSamples;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double frameMs = (double)FrameSize / SampleRate * 1000.0;
        long next = 0;
        for (int f = 0; f < (int)(4.0 * SampleRate / FrameSize); f++)
        {
            var s = new float[FrameSize];
            for (int i = 0; i < FrameSize; i++)
            {
                if (sinceBeat >= periodSamples) sinceBeat = 0;
                int t = sinceBeat++;
                s[i] = t < clickSamples
                    ? (float)(Math.Sin(2 * Math.PI * 200 * t / SampleRate) * Math.Exp(-t / (0.008 * SampleRate)) * 0.8)
                    : 0f;
            }
            audio.Emit(new AudioFrame(s, SampleRate, new AudioSourceFormat("Mock", SampleRate, 1, 32), DateTimeOffset.UtcNow));
            next += (long)(frameMs * TimeSpan.TicksPerMillisecond);
            long delay = next - sw.Elapsed.Ticks;
            if (delay > TimeSpan.TicksPerMillisecond) Thread.Sleep(TimeSpan.FromTicks(delay));
        }

        Assert.InRange(detector.CurrentBpm, 112, 128); // locked

        audio.RaiseState(AudioCaptureState.Stopped);
        Assert.Equal(0, detector.CurrentBpm); // cleared, no stale tempo lingering
    }
}
