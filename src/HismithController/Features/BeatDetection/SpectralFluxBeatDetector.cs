using NAudio.Dsp;
using HismithController.Audio;
using HismithController.Configuration;

namespace HismithController.BeatDetection;

// Detects beats via positive spectral flux in the kick/bass frequency band and
// feeds the resulting inter-beat intervals to a BpmEstimator.
//
// Algorithm: for every hop of HopSize new samples, a 512-point FFT is computed
// over the most recent FftSize samples (50 % overlap). Spectral flux is the sum
// of positive magnitude differences in the low-frequency bins since the previous
// hop. An onset is declared when flux exceeds an adaptive threshold (mean of the
// last 40 flux values × OnsetMultiplier) and the min inter-onset gap has elapsed.
//
// Threading: all processing runs synchronously on the NAudio audio capture
// thread (IAudioCaptureService.SamplesAvailable). BeatDetected subscribers must
// not block and must marshal UI / BLE work off this thread.
public sealed class SpectralFluxBeatDetector : IBeatDetector
{
    // FFT size 512: frequency resolution = 44100/512 ≈ 86.1 Hz/bin.
    // Hop size 256 (50 % overlap): one hop ≈ 5.8 ms at 44.1 kHz.
    private const int FftSize    = 512;
    private const int HopSize    = 256;
    private const int FftM       = 9;   // log2(512)
    private const int SampleRate = 44100;

    // Low-frequency band for kick/bass onset detection: 0–300 Hz.
    // At 44100/512 ≈ 86.1 Hz/bin this covers FFT bins 1, 2, 3 (~86, 172, 258 Hz).
    // LowBinHigh = ceil(300 * 512/44100) = 4, so the loop runs k = 1..3.
    private const double LowFreqMaxHz = 300.0;
    private static readonly int LowBinHigh =
        Math.Max(2, (int)Math.Ceiling(LowFreqMaxHz * FftSize / SampleRate));

    // Adaptive threshold: mean of the last FluxHistoryLen flux values × multiplier.
    private const int FluxHistoryLen = 40;

    // 200 ms minimum between onsets caps detection at 300 BPM and suppresses
    // double-triggers on the same transient across adjacent hops.
    private const double MinInterOnsetMs = 200.0;

    // IBIs above this value indicate a long gap (e.g. audio service restarted)
    // rather than a genuine slow tempo. 60000/15 ≈ 4000 ms corresponds to 15 BPM,
    // the stated minimum; anything larger resets the estimator instead of being
    // fed in as a "very slow" IBI that would skew the BPM estimate for many beats.
    private const double MaxIbiMs = 60_000.0 / 15.0; // ≈ 4000 ms

    private readonly float        _onsetMultiplier;
    private readonly BpmEstimator _bpmEstimator;

    // ── Ring buffer ───────────────────────────────────────────────────────────
    // _ringWritePos points to the oldest sample (= next slot to overwrite).
    // After each write, _ringWritePos = (pos + 1) % FftSize, so the extraction
    // loop (idx = (_ringWritePos + i) % FftSize, i = 0..FftSize-1) always yields
    // samples in oldest-to-newest order — the correct input order for the FFT.
    private readonly float[]   _ring      = new float[FftSize];
    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly float[]   _hannWindow;
    private int _ringWritePos;
    private int _samplesReceived;
    private int _hopAccumulator;

    // Previous-hop low-band magnitudes for spectral flux computation.
    // Sized LowBinHigh; index 0 (DC) is unused.
    private readonly float[] _prevMag = new float[LowBinHigh];
    private bool _hasPrevMag;

    // Adaptive threshold history ring buffer.
    private readonly double[] _fluxHistory = new double[FluxHistoryLen];
    private int _fluxHistoryPos;
    private int _fluxHistoryCount;

    // Last beat TickCount64 (ms). 0 until the first beat is detected.
    private long _lastBeatTick;

    public int   CurrentBpm => _bpmEstimator.CurrentBpm;
    // BPM estimation confidence — distinct from per-onset detection strength,
    // which is carried separately in BeatEventArgs.Confidence.
    public float Confidence => _bpmEstimator.Confidence;

    public event EventHandler<BeatEventArgs>? BeatDetected;

    public SpectralFluxBeatDetector(IAudioCaptureService audioService, AppSettings settings)
    {
        _onsetMultiplier = (float)settings.OnsetMultiplier;
        _hannWindow      = BuildHannWindow(FftSize);
        _bpmEstimator    = new BpmEstimator(
            k:                   settings.BpmWindowK,
            deviationThreshold:  settings.BpmDeviationThreshold,
            confirmTolerance:    settings.BpmConfirmationTolerance,
            emaAlpha:            settings.BpmEmaAlpha);

        // Lifetime matches the app; unsubscribing is not needed.
        audioService.SamplesAvailable += OnSamplesAvailable;
    }

    // ── Audio thread ──────────────────────────────────────────────────────────

    private void OnSamplesAvailable(object? sender, AudioFrame frame)
    {
        var samples = frame.MonoSamples;
        int pos = 0;

        while (pos < samples.Length)
        {
            int toCopy = Math.Min(HopSize - _hopAccumulator, samples.Length - pos);

            for (int i = 0; i < toCopy; i++)
            {
                _ring[_ringWritePos] = samples[pos + i];
                _ringWritePos = (_ringWritePos + 1) % FftSize;
            }

            _hopAccumulator  += toCopy;
            _samplesReceived += toCopy;
            pos              += toCopy;

            if (_hopAccumulator >= HopSize)
            {
                _hopAccumulator = 0;
                // Defer FFT until the ring is fully populated; a partial window
                // would produce misleading low-frequency bias.
                if (_samplesReceived >= FftSize)
                    RunHop();
            }
        }
    }

    private void RunHop()
    {
        // Copy ring into FFT buffer in chronological order (oldest → newest)
        // and apply the Hann window to reduce spectral leakage.
        for (int i = 0; i < FftSize; i++)
        {
            int idx = (_ringWritePos + i) % FftSize;
            _fftBuffer[i].X = _ring[idx] * _hannWindow[i];
            _fftBuffer[i].Y = 0f;
        }

        FastFourierTransform.FFT(forward: true, m: FftM, data: _fftBuffer);

        // On the first hop just capture baseline magnitudes; skip flux so the
        // history does not get a spurious zero entry (which lowers the threshold
        // and risks a false positive on the very next hop).
        if (!_hasPrevMag)
        {
            StoreMagnitudes();
            _hasPrevMag = true;
            return;
        }

        // Positive spectral flux over the kick/bass band.
        double flux = 0.0;
        for (int k = 1; k < LowBinHigh; k++)
        {
            double re  = _fftBuffer[k].X;
            double im  = _fftBuffer[k].Y;
            float  mag = (float)(Math.Sqrt(re * re + im * im) / (FftSize / 2.0));

            double diff = mag - _prevMag[k];
            if (diff > 0.0) flux += diff;

            _prevMag[k] = mag;
        }

        // Append flux to the adaptive threshold history.
        _fluxHistory[_fluxHistoryPos] = flux;
        _fluxHistoryPos = (_fluxHistoryPos + 1) % FluxHistoryLen;
        if (_fluxHistoryCount < FluxHistoryLen) _fluxHistoryCount++;

        // Require a minimal history before triggering to avoid false positives
        // during the initial fill period.
        if (_fluxHistoryCount < 3) return;

        double fluxMean = 0.0;
        for (int i = 0; i < _fluxHistoryCount; i++)
            fluxMean += _fluxHistory[i];
        fluxMean /= _fluxHistoryCount;

        double threshold = fluxMean * _onsetMultiplier;
        if (threshold <= 0.0 || flux <= threshold) return;

        // Min inter-onset interval guard.
        long   now     = Environment.TickCount64;
        double elapsed = _lastBeatTick == 0 ? double.MaxValue : now - _lastBeatTick;
        if (elapsed < MinInterOnsetMs) return;

        // Feed the inter-beat interval to the BPM estimator.
        if (_lastBeatTick != 0)
        {
            if (elapsed < MaxIbiMs)
                _bpmEstimator.AddIbi(elapsed);
            else
                // Long gap (e.g. audio paused / restarted): stale history would
                // skew the estimate for many beats; better to restart fresh.
                _bpmEstimator.Reset();
        }

        _lastBeatTick = now;

        // Onset confidence: how far above the adaptive threshold this peak was.
        // 0 = just at threshold, 1 = 2× threshold.
        float conf = (float)Math.Min(1.0, (flux / threshold - 1.0) * 2.0);

        BeatDetected?.Invoke(this, new BeatEventArgs(DateTimeOffset.UtcNow, conf));
    }

    // Store current low-band magnitudes as the baseline for the next hop's flux.
    private void StoreMagnitudes()
    {
        for (int k = 1; k < LowBinHigh; k++)
        {
            double re = _fftBuffer[k].X;
            double im = _fftBuffer[k].Y;
            _prevMag[k] = (float)(Math.Sqrt(re * re + im * im) / (FftSize / 2.0));
        }
    }

    private static float[] BuildHannWindow(int size)
    {
        var w = new float[size];
        for (int i = 0; i < size; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / size));
        return w;
    }
}
