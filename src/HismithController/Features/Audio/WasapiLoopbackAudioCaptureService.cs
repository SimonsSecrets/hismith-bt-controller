using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HismithController.Audio;

internal sealed class WasapiLoopbackAudioCaptureService : IAudioCaptureService
{
    private readonly ILogger<WasapiLoopbackAudioCaptureService> _logger;
    private WasapiLoopbackCapture? _capture;

    // volatile: written by SetState (called from the audio thread and from
    // Task.Run inside StopAsync) and read by the public State property and
    // OnDataAvailable (any thread). volatile gives the required visibility
    // without the overhead of a full lock for this single enum-sized value.
    private volatile AudioCaptureState _state = AudioCaptureState.Stopped;

    // All downstream consumers (beat detector, spectrum analyser) are expressed
    // in samples at this fixed rate. Normalising here means those components
    // can hard-code FFT sizes, hop sizes, and window durations in samples and
    // have them always carry the same wall-clock meaning regardless of the
    // system mixer's actual output rate.
    private const int CanonicalSampleRate = 44100;

    // Sliding-window RMS for silence detection — ring buffer of squared samples.
    // Window = 500 ms. We add each incoming squared sample and subtract the one
    // it overwrites, keeping a running sum in O(1) per sample.
    // Math.Max(0, _rmsSum) guards against tiny negative values caused by
    // accumulated floating-point rounding errors producing NaN in sqrt().
    private const int RmsWindowSamples = CanonicalSampleRate / 2; // 500 ms
    private const float SilenceThreshold = 0.0001f;               // ≈ -80 dBFS
    private readonly float[] _rmsRing = new float[RmsWindowSamples];
    private int _rmsRingPos;
    private double _rmsSum;

    // Watchdog timer: detects when the WASAPI loopback stops firing DataAvailable
    // callbacks. WASAPI loopback does NOT fire callbacks during true silence
    // (hardware-level audio rendering is idle), so UpdateSilenceDetection never
    // runs and the state would stay at Running indefinitely after music stops.
    // The watchdog fires every SilenceTimeoutMs/2 ms and checks whether a full
    // SilenceTimeoutMs has elapsed without any DataAvailable callback; if so, it
    // clears the RMS ring and forces a NoSignal transition.
    private System.Threading.Timer? _silenceWatchdog;
    private long _lastDataTick; // written by audio thread; read by timer thread
    // 1.5 s chosen so the idle-decay timer in SpectrumAnalyzer has enough time
    // to bring all bars to the visual floor (0 px) before the overlay appears.
    // Math: a bin starting at −47.5 dB (75 % bar height) needs ~42 idle-timer
    // ticks × 35 ms = ~1470 ms to decay below DbFloor (−130 dB). Bars at real-
    // audio levels (< −60 dB) decay to the floor in well under 1 s.
    private const int SilenceTimeoutMs = 1500; // 1.5 s without callbacks → NoSignal

    // KSDATAFORMAT_SUBTYPE_* GUIDs from the Windows multimedia kernel.
    // WaveFormatExtensible.SubFormat identifies the actual sample layout when
    // the top-level WaveFormat.Encoding is WaveFormatEncoding.Extensible.
    // Both sub-formats appear in the wild from real WASAPI loopback sessions.
    private static readonly Guid SubFormatPcm   = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubFormatFloat = new("00000003-0000-0010-8000-00aa00389b71");

    public WasapiLoopbackAudioCaptureService(ILogger<WasapiLoopbackAudioCaptureService> logger)
    {
        _logger = logger;
    }

    public AudioCaptureState State => _state;

    public event EventHandler<AudioCaptureState>? StateChanged;
    public event EventHandler<AudioFrame>? SamplesAvailable;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != AudioCaptureState.Stopped)
            return Task.CompletedTask;

        _capture = new WasapiLoopbackCapture();

        // Log the raw source format so we can verify which decode branch was
        // exercised and spot any unexpected system mixer configurations.
        _logger.LogInformation("Audio capture starting. Source format: {Format}", _capture.WaveFormat);

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        // Stay in Starting until the first DataAvailable callback; UpdateSilenceDetection
        // will transition to Running or NoSignal based on the actual RMS of the first frame.
        // Transitioning to Running here (before any audio data) would keep HasAudio=true
        // even during system silence, because WASAPI loopback doesn't fire DataAvailable
        // callbacks when no audio is actively rendering — the silence check never runs.
        SetState(AudioCaptureState.Starting);
        _capture.StartRecording();

        // Arm the watchdog. Stamp _lastDataTick now so the watchdog does not
        // immediately fire before the first DataAvailable callback arrives.
        Volatile.Write(ref _lastDataTick, Environment.TickCount64);
        _silenceWatchdog = new System.Threading.Timer(
            OnSilenceWatchdog, null,
            dueTime: SilenceTimeoutMs, period: SilenceTimeoutMs / 2);

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        // Interlocked.Exchange atomically grabs _capture and replaces it with
        // null. If two callers race into StopAsync, only one gets a non-null
        // value; the other exits immediately, preventing a double-dispose.
        var capture = Interlocked.Exchange(ref _capture, null);
        if (capture is null)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            // Dispose the watchdog first so it cannot fire a state transition
            // after we have started tearing down the capture pipeline.
            Interlocked.Exchange(ref _silenceWatchdog, null)?.Dispose();

            // Unsubscribe before calling StopRecording so NAudio cannot fire
            // DataAvailable callbacks against a capture object mid-teardown.
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnRecordingStopped;

            // StopRecording() calls Thread.Join() on the NAudio capture thread,
            // so it blocks. Wrapping in Task.Run keeps the UI thread free.
            capture.StopRecording();
            capture.Dispose();
            SetState(AudioCaptureState.Stopped);
        });
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ── Audio thread ──────────────────────────────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        // Refresh the watchdog timestamp on every callback so the timer knows
        // audio is still actively rendering.
        Volatile.Write(ref _lastDataTick, Environment.TickCount64);

        // Snapshot WaveFormat via _capture? before using it: StopAsync can null
        // _capture on the UI thread while this callback is executing on the
        // NAudio audio thread.
        var wf = _capture?.WaveFormat;
        if (wf is null) return;

        float[] mono     = DecodeAndDownmixToMono(e.Buffer, e.BytesRecorded, wf);
        float[] resampled = ResampleTo44100(mono, wf.SampleRate);

        UpdateSilenceDetection(resampled);

        // Snapshot volatile _state once to get a consistent value for this frame.
        var currentState = _state;

        // Publish in both Running and NoSignal: the spectrum visualiser must keep
        // moving (displaying silence) even when no signal is present. Only
        // suppress frames in Stopped/Starting/Error to avoid sending stale data
        // after teardown or before the first real callback.
        if (currentState is AudioCaptureState.Running or AudioCaptureState.NoSignal)
        {
            var sourceFormat = new AudioSourceFormat(
                wf.Encoding.ToString(),
                wf.SampleRate,
                wf.Channels,
                wf.BitsPerSample);

            SamplesAvailable?.Invoke(this, new AudioFrame(
                resampled, CanonicalSampleRate, sourceFormat, DateTimeOffset.UtcNow));
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // NAudio fires RecordingStopped with e.Exception == null on a clean stop
        // (triggered by our own StopRecording() call). Only log and signal Error
        // on an unexpected stop so the UI can surface the problem.
        if (e.Exception is not null)
        {
            _logger.LogError(e.Exception, "Audio capture stopped unexpectedly.");
            SetState(AudioCaptureState.Error);
        }
    }

    // ── Silence watchdog ──────────────────────────────────────────────────────

    private void OnSilenceWatchdog(object? _)
    {
        // Only act when audio was (or might have been) present.
        var s = _state;
        if (s is not (AudioCaptureState.Running or AudioCaptureState.Starting))
            return;

        long elapsed = Environment.TickCount64 - Volatile.Read(ref _lastDataTick);
        if (elapsed < SilenceTimeoutMs)
            return;

        // No DataAvailable callback for SilenceTimeoutMs: the hardware render
        // stream went idle. Clear the RMS ring so stale energy from the previous
        // audio session does not bleed into the silence check when audio resumes.
        Array.Clear(_rmsRing, 0, _rmsRing.Length);
        _rmsRingPos = 0;
        _rmsSum     = 0.0;

        SetState(AudioCaptureState.NoSignal);
    }

    // ── Decoding ──────────────────────────────────────────────────────────────

    // Returns a mono float[] in the range [-1, 1] at the source sample rate.
    // All channel averaging (downmix) happens here so downstream components
    // always see single-channel data.
    private static float[] DecodeAndDownmixToMono(byte[] buffer, int bytesRecorded, WaveFormat wf)
    {
        int channels     = wf.Channels;
        WaveFormatEncoding encoding = wf.Encoding;
        int bitsPerSample = wf.BitsPerSample;

        // Extensible wraps the true format in SubFormat; resolve before dispatch.
        if (encoding == WaveFormatEncoding.Extensible && wf is WaveFormatExtensible ext)
        {
            if (ext.SubFormat == SubFormatFloat && bitsPerSample == 32)
                encoding = WaveFormatEncoding.IeeeFloat;
            else if (ext.SubFormat == SubFormatPcm)
                encoding = WaveFormatEncoding.Pcm;
            else
                throw new NotSupportedException(
                    $"Extensible sub-format {ext.SubFormat} ({bitsPerSample}-bit) is not supported.");
        }

        if (encoding == WaveFormatEncoding.IeeeFloat && bitsPerSample == 32)
            return DecodeFloat32(buffer, bytesRecorded, channels);

        if (encoding == WaveFormatEncoding.Pcm)
            return DecodePcm(buffer, bytesRecorded, channels, bitsPerSample);

        throw new NotSupportedException(
            $"Audio format {wf.Encoding} {bitsPerSample}-bit is not supported.");
    }

    // IEEE 754 float samples are stored natively; just reinterpret and average.
    private static float[] DecodeFloat32(byte[] buffer, int bytesRecorded, int channels)
    {
        int frameCount = bytesRecorded / (channels * 4);
        var mono = new float[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            float sum = 0f;
            for (int ch = 0; ch < channels; ch++)
                sum += BitConverter.ToSingle(buffer, (i * channels + ch) * 4);
            mono[i] = sum / channels;
        }
        return mono;
    }

    // Handles 16-, 24-, and 32-bit signed integer PCM, all little-endian.
    private static float[] DecodePcm(byte[] buffer, int bytesRecorded, int channels, int bitsPerSample)
    {
        int bytesPerSample = bitsPerSample / 8;
        int frameCount     = bytesRecorded / (channels * bytesPerSample);
        var mono = new float[frameCount];
        for (int i = 0; i < frameCount; i++)
        {
            float sum = 0f;
            for (int ch = 0; ch < channels; ch++)
            {
                int offset = (i * channels + ch) * bytesPerSample;
                sum += bitsPerSample switch
                {
                    // Divisors are 2^(N-1): the full-scale signed integer range
                    // maps to [-1, ~1) rather than [-1, 1] because the negative
                    // extreme (-2^(N-1)) has no positive counterpart.
                    16 => BitConverter.ToInt16(buffer, offset) / 32768f,
                    24 => ReadPcm24(buffer, offset) / 8388608f,
                    32 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                    _ => throw new NotSupportedException($"PCM {bitsPerSample}-bit is not supported.")
                };
            }
            mono[i] = sum / channels;
        }
        return mono;
    }

    // Reads a 3-byte little-endian integer and sign-extends it to 32 bits.
    // Bit 23 is the sign bit; setting bits 24-31 to 1 when it is set produces
    // the correct two's complement 32-bit value.
    private static float ReadPcm24(byte[] buf, int offset)
    {
        int value = buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16);
        if ((value & 0x800000) != 0)
            value |= unchecked((int)0xFF000000);
        return value;
    }

    // ── Resampling ────────────────────────────────────────────────────────────

    // Linear interpolation to CanonicalSampleRate (44100 Hz).
    // Linear is adequate for the beat detector (low-frequency content) and for
    // the spectrum visualiser (approximate display). A polyphase or sinc
    // resampler would be higher quality but is not worth the additional CPU
    // cost on the audio callback thread where we must stay under ~2 ms.
    private static float[] ResampleTo44100(float[] input, int sourceSampleRate)
    {
        if (sourceSampleRate == CanonicalSampleRate)
            return input;

        // ratio > 1 means source is faster (e.g. 48000 → 44100: ratio ≈ 1.088),
        // so each output sample maps to slightly more than one input sample.
        double ratio = (double)sourceSampleRate / CanonicalSampleRate;

        // Ceiling covers the last partial input block rather than silently
        // dropping its final samples.
        int outputLength = (int)Math.Ceiling(input.Length / ratio);
        var output = new float[outputLength];

        for (int i = 0; i < outputLength; i++)
        {
            double sourcePos = i * ratio;
            int    srcIdx    = (int)sourcePos;
            double frac      = sourcePos - srcIdx;
            float  a = srcIdx     < input.Length ? input[srcIdx]     : 0f;
            float  b = srcIdx + 1 < input.Length ? input[srcIdx + 1] : 0f;
            output[i] = (float)(a + frac * (b - a));
        }

        return output;
    }

    // ── Silence detection ─────────────────────────────────────────────────────

    private void UpdateSilenceDetection(float[] monoSamples)
    {
        foreach (float s in monoSamples)
        {
            float sq = s * s;
            // Subtract the squared sample we are about to overwrite so the
            // running sum always reflects exactly RmsWindowSamples samples.
            _rmsSum -= _rmsRing[_rmsRingPos];
            _rmsRing[_rmsRingPos] = sq;
            _rmsSum  += sq;
            _rmsRingPos = (_rmsRingPos + 1) % RmsWindowSamples;
        }

        double rms      = Math.Sqrt(Math.Max(0.0, _rmsSum) / RmsWindowSamples);
        bool   isSilent = rms < SilenceThreshold;

        // Starting is the "pre-first-callback" state. The first DataAvailable always
        // exits Starting: silent first frame → NoSignal (overlay shown), non-silent →
        // Running (overlay hidden, bars live). Even with a mostly-empty ring the RMS
        // denominator is the full window size, so even a single loud callback drives
        // rms well above SilenceThreshold and exits Starting on the very first frame.
        if (isSilent && _state is AudioCaptureState.Running or AudioCaptureState.Starting)
            SetState(AudioCaptureState.NoSignal);
        else if (!isSilent && _state is AudioCaptureState.NoSignal or AudioCaptureState.Starting)
            SetState(AudioCaptureState.Running);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private void SetState(AudioCaptureState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }
}
