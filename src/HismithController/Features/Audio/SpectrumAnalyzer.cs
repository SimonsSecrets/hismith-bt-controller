using NAudio.Dsp;

namespace HismithController.Audio;

// Maintains a rolling short-time FFT over mono audio frames and emits a
// 56-bin logarithmic magnitude spectrum at ~30 fps for the visualizer.
//
// Threading: OnSamplesAvailable (and therefore SpectrumUpdated) fires on
// whichever thread IAudioCaptureService raises SamplesAvailable — the NAudio
// audio callback thread for real capture, or a Task.Run thread for the mock.
// Callers (SoundModeViewModel) must marshal SpectrumUpdated to the UI thread.
internal sealed class SpectrumAnalyzer
{
    // FFT size 1024: frequency resolution = 44100/1024 ≈ 43 Hz/bin.
    // Hop size 512: 50% overlap gives two FFTs per window, balancing temporal
    // resolution against CPU cost. At 44100/512 ≈ 86 hops/sec we can emit
    // 3-hop batches for ~28.7 fps — close enough to the 30 fps target.
    private const int FftSize = 1024;
    private const int HopSize = 512;
    private const int FftM    = 10;         // log2(FftSize)
    private const int BinCount = 56;        // must match VizBars N in modes.jsx
    private const int SampleRate = 44100;

    // Smoothing: fast attack (immediate jump to louder value), slow decay.
    // 0.85 per FFT frame ≈ 0.85^86 ≈ 0.000027 remaining after one second of silence,
    // giving a perceptible ~0.5 s visual tail before a bar returns to zero.
    private const float DecayFactor = 0.85f;

    // Emit SpectrumUpdated every EmitEveryNHops FFTs ≈ 86/3 ≈ 28.7 fps.
    private const int EmitEveryNHops = 3;

    // Frequency range for the 56 log bins. 20 Hz is below the FFT resolution
    // (bin 1 ≈ 43 Hz) but we clamp, so the first few output bins map to the
    // same low FFT bins — that is correct behaviour for a log-scaled visualizer.
    private const double FreqMin = 20.0;
    private const double FreqMax = 20_000.0;

    // Precomputed tables — built once in the constructor.
    private readonly float[] _hannWindow;
    private readonly (int low, int high)[] _logBins;

    // Reusable FFT work buffer. NAudio's FFT operates in-place on Complex[].
    private readonly Complex[] _fftBuffer = new Complex[FftSize];

    // Ring buffer: always holds the most recent FftSize samples.
    // _ringWritePos is the index of the *oldest* sample (= next write position).
    // After writing sample S, advance _ringWritePos = (pos + 1) % FftSize so
    // that _ringWritePos always points to the slot about to be overwritten next,
    // which is the oldest sample when the buffer is full.
    private readonly float[] _ring = new float[FftSize];
    private int _ringWritePos;

    // Track total samples ever received so we skip FFTs until the ring is full.
    private int _samplesReceived;

    // Counts new samples since the last FFT; triggers an FFT every HopSize.
    private int _hopAccumulator;

    // Per-bin smoothed magnitude (the running state across FFT frames).
    private readonly double[] _smoothed = new double[BinCount];

    // How many FFT frames have been computed since the last SpectrumUpdated event.
    private int _hopsSinceEmit;

    // SpectrumUpdated fires at ~30 fps. The array contains BinCount (56) values,
    // each in [0, ~1], normalised by FftSize/2.
    // The array reference is a fresh clone each firing — safe for subscribers to hold.
    public event EventHandler<double[]>? SpectrumUpdated;

    public SpectrumAnalyzer(IAudioCaptureService audioService)
    {
        _hannWindow = BuildHannWindow(FftSize);
        _logBins    = BuildLogBins();

        // Subscribe for the app's lifetime; both have singleton scope in DI.
        audioService.SamplesAvailable += OnSamplesAvailable;
    }

    // ── Audio thread ──────────────────────────────────────────────────────────

    private void OnSamplesAvailable(object? sender, AudioFrame frame)
    {
        var samples = frame.MonoSamples;
        int pos = 0;

        while (pos < samples.Length)
        {
            // Copy up to HopSize samples at a time so we trigger RunFft exactly
            // when a full hop's worth of new samples has been accumulated.
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

                // Don't run until the ring is fully populated; early partial
                // windows would produce misleading low-frequency bias.
                if (_samplesReceived >= FftSize)
                    RunFft();
            }
        }
    }

    private void RunFft()
    {
        // Extract the most recent FftSize samples from the ring in chronological
        // order (oldest → newest) and apply the Hann window.
        // _ringWritePos currently points to the oldest sample (next to overwrite).
        for (int i = 0; i < FftSize; i++)
        {
            int idx = (_ringWritePos + i) % FftSize;
            _fftBuffer[i].X = _ring[idx] * _hannWindow[i];
            _fftBuffer[i].Y = 0f;
        }

        // In-place forward FFT. After this call, _fftBuffer[k] holds the
        // complex DFT coefficient for frequency k * SampleRate / FftSize.
        FastFourierTransform.FFT(forward: true, m: FftM, data: _fftBuffer);

        // Map positive-frequency bins [1, FftSize/2) into the 56 log groups,
        // taking the peak magnitude within each group for visual punch.
        for (int b = 0; b < BinCount; b++)
        {
            var (kLow, kHigh) = _logBins[b];
            double peak = 0.0;
            for (int k = kLow; k < kHigh; k++)
            {
                // Magnitude = sqrt(Re² + Im²). Cast to double to avoid float
                // precision loss when squaring values near the edge of float range.
                double re  = _fftBuffer[k].X;
                double im  = _fftBuffer[k].Y;
                double mag = Math.Sqrt(re * re + im * im) / (FftSize / 2.0);
                if (mag > peak) peak = mag;
            }

            // Fast attack: jump immediately if the new peak is higher.
            // Slow decay: multiply by DecayFactor when the signal has dropped.
            _smoothed[b] = Math.Max(peak, _smoothed[b] * DecayFactor);
        }

        // Throttle: only publish every EmitEveryNHops FFTs (~30 fps).
        _hopsSinceEmit++;
        if (_hopsSinceEmit < EmitEveryNHops)
            return;

        _hopsSinceEmit = 0;

        // Clone so the subscriber can hold a reference without corrupting _smoothed.
        SpectrumUpdated?.Invoke(this, (double[])_smoothed.Clone());
    }

    // ── Precomputation ────────────────────────────────────────────────────────

    // Periodic Hann window: w[n] = 0.5 * (1 − cos(2π·n/N)).
    // Reduces spectral leakage caused by treating the finite sample block
    // as if it repeats periodically (which is what the DFT assumes).
    private static float[] BuildHannWindow(int size)
    {
        var w = new float[size];
        for (int i = 0; i < size; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / size));
        return w;
    }

    // Builds a table of FFT bin index ranges for each of the 56 logarithmic
    // output bins. The frequency axis is divided evenly in log space between
    // FreqMin and FreqMax; each entry is the half-open FFT bin range [low, high).
    private static (int low, int high)[] BuildLogBins()
    {
        var bins = new (int low, int high)[BinCount];
        for (int i = 0; i < BinCount; i++)
        {
            double fLow  = FreqMin * Math.Pow(FreqMax / FreqMin, (double)i       / BinCount);
            double fHigh = FreqMin * Math.Pow(FreqMax / FreqMin, (double)(i + 1) / BinCount);

            // Convert Hz to FFT bin index (round toward the bin centre).
            int kLow  = (int)Math.Floor(fLow  * FftSize / SampleRate);
            int kHigh = (int)Math.Ceiling(fHigh * FftSize / SampleRate);

            // Clamp to the valid positive-frequency range, and guarantee that
            // every group spans at least one bin even when low-frequency bins
            // are narrower than one FFT bin.
            kLow  = Math.Clamp(kLow,      1,          FftSize / 2 - 1);
            kHigh = Math.Clamp(kHigh, kLow + 1,       FftSize / 2);

            bins[i] = (kLow, kHigh);
        }
        return bins;
    }
}
