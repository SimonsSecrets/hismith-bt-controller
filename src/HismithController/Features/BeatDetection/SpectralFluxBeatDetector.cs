using NAudio.Dsp;
using HismithController.Audio;
using HismithController.Configuration;

namespace HismithController.BeatDetection;

// Detects beats via positive spectral flux in the kick/bass frequency band.
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

    // K=4 IBI ring buffer for a basic median-based BPM estimate (Phase 2).
    // The full BpmEstimator with change-detection is wired in Phase 2 task 2.3.
    private const int IbiBufferLen = 4;

    private readonly float _onsetMultiplier;

    // ── Ring buffer ───────────────────────────────────────────────────────────
    // _ringWritePos points to the oldest sample (= next slot to overwrite).
    // After each write, _ringWritePos = (pos + 1) % FftSize, so the extraction
    // loop (idx = (_ringWritePos + i) % FftSize, i = 0..FftSize-1) always yields
    // samples in oldest-to-newest order — the correct input order for the FFT.
    private readonly float[] _ring         = new float[FftSize];
    private readonly Complex[] _fftBuffer  = new Complex[FftSize];
    private readonly float[] _hannWindow;
    private int  _ringWritePos;
    private int  _samplesReceived;
    private int  _hopAccumulator;

    // Previous-hop low-band magnitudes for spectral flux computation.
    // Sized LowBinHigh; index 0 (DC) is unused.
    private readonly float[] _prevMag = new float[LowBinHigh];
    private bool _hasPrevMag;

    // Adaptive threshold history ring buffer.
    private readonly double[] _fluxHistory  = new double[FluxHistoryLen];
    private int  _fluxHistoryPos;
    private int  _fluxHistoryCount;

    // Inter-onset timing: last beat TickCount64 (ms) and timestamp.
    // _lastBeatTick == 0 until the first beat is detected.
    private long _lastBeatTick;

    // IBI ring buffer (milliseconds) for median BPM estimation.
    private readonly double[] _ibis = new double[IbiBufferLen];
    private int _ibiPos;
    private int _ibiCount;

    // volatile: written by the audio thread, read by UI / ViewModel threads.
    private volatile int   _currentBpm;
    private volatile float _confidence;

    public int   CurrentBpm => _currentBpm;
    public float Confidence => _confidence;

    public event EventHandler<BeatEventArgs>? BeatDetected;

    public SpectralFluxBeatDetector(IAudioCaptureService audioService, AppSettings settings)
    {
        _onsetMultiplier = (float)settings.OnsetMultiplier;
        _hannWindow      = BuildHannWindow(FftSize);

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
        long now       = Environment.TickCount64;
        double elapsed = _lastBeatTick == 0 ? double.MaxValue : now - _lastBeatTick;
        if (elapsed < MinInterOnsetMs) return;

        // Record IBI for BPM estimation.
        if (_lastBeatTick != 0)
        {
            _ibis[_ibiPos] = elapsed;
            _ibiPos = (_ibiPos + 1) % IbiBufferLen;
            if (_ibiCount < IbiBufferLen) _ibiCount++;
            _currentBpm = ComputeMedianBpm();
        }

        _lastBeatTick = now;

        // Confidence: how far above threshold the onset landed.
        // 0 = just at threshold, 1 = twice the threshold.
        float conf = (float)Math.Min(1.0, (flux / threshold - 1.0) * 2.0);
        _confidence = conf;

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

    // Median of the K most recent IBIs, converted to BPM.
    // Median is robust to a single mis-detected beat; at K=4 it tolerates one
    // outlier without the percentile-trim instability that arises at small N.
    private int ComputeMedianBpm()
    {
        if (_ibiCount < 2) return 0;

        // Insertion sort on a stack-allocated copy (K=4, negligible overhead).
        Span<double> temp = stackalloc double[IbiBufferLen];
        int n = 0;
        for (int i = 0; i < _ibiCount; i++)
            temp[n++] = _ibis[i];

        for (int i = 1; i < n; i++)
        {
            double key = temp[i];
            int    j   = i - 1;
            while (j >= 0 && temp[j] > key) { temp[j + 1] = temp[j]; j--; }
            temp[j + 1] = key;
        }

        double medianMs = n % 2 == 0
            ? (temp[n / 2 - 1] + temp[n / 2]) / 2.0
            : temp[n / 2];

        // Clamp to the supported BPM range (15–240) rather than returning
        // impossible values on spurious double-triggers or very slow sources.
        return Math.Clamp((int)Math.Round(60000.0 / medianMs), 15, 240);
    }

    private static float[] BuildHannWindow(int size)
    {
        var w = new float[size];
        for (int i = 0; i < size; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / size));
        return w;
    }
}
