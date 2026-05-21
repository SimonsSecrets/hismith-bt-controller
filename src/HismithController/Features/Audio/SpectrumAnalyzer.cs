using NAudio.Dsp;

namespace HismithController.Audio;

// Maintains a rolling short-time FFT over mono audio frames and emits a
// 56-bin logarithmic magnitude spectrum at ~30 fps for the visualizer.
//
// Threading: OnSamplesAvailable (and therefore SpectrumUpdated) fires on
// whichever thread IAudioCaptureService raises SamplesAvailable — the NAudio
// audio callback thread for real capture, or a Task.Run thread for the mock.
// Callers (SoundModeViewModel) must marshal SpectrumUpdated to the UI thread.
public sealed class SpectrumAnalyzer
{
    // FFT size 4096: frequency resolution = 44100/4096 ≈ 10.8 Hz/bin.
    // A smaller FFT (e.g. 1024, ≈43 Hz/bin) causes the first ~11 log-scale
    // output bins to clamp to the same FFT bin (k=1 ≈ 43 Hz) because the
    // log spacing at 20 Hz is only ~2.6 Hz — far narrower than one 43 Hz bin.
    // At 10.8 Hz/bin the lowest bars each resolve a distinct frequency slice,
    // reducing identical-bar groups to at most 2 adjacent bins.
    // Hop size stays at 512 so the FFT fires at 44100/512 ≈ 86 hops/sec and
    // EmitEveryNHops batches keep the ~28.7 fps emit rate unchanged.
    // CPU cost per hop scales as O(N log N): 4096-point FFT ≈ 5× a 1024-point
    // FFT but still well under 1 ms on any modern CPU — not a concern.
    private const int FftSize = 4096;
    private const int HopSize = 512;
    private const int FftM    = 12;         // log2(FftSize)
    private const int BinCount = 56;        // must match VizBars N in modes.jsx
    private const int SampleRate = 44100;

    // Smoothing: fast attack (immediate jump to louder value), slow decay.
    // 0.92 per FFT frame ≈ 0.92^86 ≈ 0.00077 remaining after one second of silence.
    // With the log-scale converter's −130 dB floor, a transient bin at −100 dBFS
    // decays below the floor in ~490 ms — about twice as long as 0.85 would give,
    // which keeps brief high-frequency transients (cymbals, hi-hats) visible long
    // enough to register rather than flickering for a single frame.
    private const float DecayFactor = 0.92f;

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

    // Idle-decay timer: continues applying DecayFactor to _smoothed even when
    // WASAPI stops firing DataAvailable callbacks (hardware silence). Without it,
    // the smoothed bins — and therefore the visualiser bars — freeze at their last
    // value rather than animating to zero.
    // The timer fires at the same ~30 fps cadence as normal FFT emission. Each
    // tick checks whether new samples have arrived recently; if not, it applies
    // EmitEveryNHops frames of decay and fires SpectrumUpdated so WPF sees the
    // bars shrinking continuously to zero.
    //
    // Thread safety: _smoothed is written by RunFft (audio thread) and by
    // OnIdleDecay (timer thread). The elapsed-time guard ensures the two are
    // mutually exclusive in practice — if samples are arriving the guard returns
    // early; if not, the audio thread is dormant. A lock would add per-frame
    // overhead on the hot audio path for a visualiser whose only failure mode is
    // a slightly wrong bar height for one frame — not justified.
    private long _lastSampleTick;                        // Volatile.Write from audio thread
    private readonly System.Threading.Timer _idleTimer;

    // Interval matches EmitEveryNHops hops at 44100/512 ≈ 34.8 ms ≈ 28.7 fps.
    private const int IdleIntervalMs = 35;

    // Three-hop compound decay: same total smoothing as EmitEveryNHops normal FFT
    // frames applied in one timer tick. 0.92^3 ≈ 0.779.
    private static readonly double DecayFactor3 = Math.Pow(DecayFactor, EmitEveryNHops);

    // Linear equivalent of DbFloor (−130 dB): 10^(−130/20) ≈ 3.16e-7.
    // Bins below this produce 0 px bar height; used to stop idle emissions
    // once all bars have fully decayed.
    private const double DbFloorLinear = 3.162e-7;

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

        // Stamp now so the idle timer does not trigger a decay run before the
        // first DataAvailable callback arrives.
        _lastSampleTick = Environment.TickCount64;
        _idleTimer = new System.Threading.Timer(
            OnIdleDecay, null,
            dueTime: IdleIntervalMs, period: IdleIntervalMs);
    }

    // ── Audio thread ──────────────────────────────────────────────────────────

    private void OnSamplesAvailable(object? sender, AudioFrame frame)
    {
        // Refresh the timestamp so OnIdleDecay knows active audio is arriving.
        Volatile.Write(ref _lastSampleTick, Environment.TickCount64);

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

    // ── Idle decay ────────────────────────────────────────────────────────────

    private void OnIdleDecay(object? _)
    {
        // Skip when the audio thread is actively supplying samples — RunFft
        // handles decay and emission there. The 2× window absorbs a single
        // late or skipped callback without falsely entering idle mode.
        long elapsed = Environment.TickCount64 - Volatile.Read(ref _lastSampleTick);
        if (elapsed < IdleIntervalMs * 2) return;

        // Don't emit before the ring is fully populated; partial windows
        // produce misleading low-frequency bias.
        if (_samplesReceived < FftSize) return;

        // Check visibility BEFORE applying decay. If we checked after, the tick
        // where a bin first crosses DbFloorLinear would decay the value below the
        // floor but then skip the emit — leaving the UI on the previous frame
        // whose value was just above the floor (→ 1–3 px frozen bar). Checking
        // first ensures we always emit one final frame with the post-decay (sub-
        // floor, 0 px) values, driving the bar cleanly to zero before we stop.
        bool anyVisible = false;
        for (int b = 0; b < BinCount; b++)
            if (_smoothed[b] > DbFloorLinear) { anyVisible = true; break; }

        if (!anyVisible) return; // All bins already at floor; nothing to update.

        for (int b = 0; b < BinCount; b++)
            _smoothed[b] *= DecayFactor3;

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
