using NAudio.Dsp;
using HismithController.Audio;
using HismithController.Configuration;

namespace HismithController.BeatDetection;

// Detects beats via positive spectral flux in the kick/bass frequency band and
// estimates tempo from the periodicity of the onset-strength envelope (OSF).
//
// Algorithm: for every hop of HopSize new samples, a 512-point FFT is computed
// over the most recent FftSize samples (50 % overlap). Spectral flux is the sum
// of positive magnitude differences in the low-frequency bins since the previous
// hop. An onset (BeatDetected, for the visual pulse / liveness) is declared when a
// flux local maximum exceeds an adaptive threshold (mean of the last 40 flux values
// × OnsetMultiplier) and the min inter-onset gap has elapsed.
//
// Tempo (CurrentBpm) is NOT derived from discrete onset intervals — those jitter
// badly on real input (e.g. a metronome tick with little low-frequency energy).
// Instead each hop's flux is appended to a continuous OSF, and a background timer
// runs AutocorrelationTempoEstimator over it: robust to noisy onsets because it
// keys off the dominant period, not individual detections. A sparsity classifier
// decides only whether to octave-fold (dense music) or not (sparse click train).
// The estimate is then passed through TempoSmoother before it is published, which
// gates large upward jumps behind a few cycles of confirmation so the transient
// spike during an input tempo change does not reach CurrentBpm.
//
// Threading: flux/onset processing runs synchronously on the NAudio audio capture
// thread (IAudioCaptureService.SamplesAvailable); the autocorrelation runs on a
// System.Threading.Timer thread. BeatDetected subscribers must not block and must
// marshal UI / BLE work off the audio thread.
public sealed class SpectralFluxBeatDetector : IBeatDetector
{
    // FFT size 512: frequency resolution = 44100/512 ≈ 86.1 Hz/bin.
    // Hop size 256 (50 % overlap): one hop ≈ 5.8 ms at 44.1 kHz.
    private const int FftSize    = 512;
    private const int HopSize    = 256;
    private const int FftM       = 9;   // log2(512)
    private const int SampleRate = 44100;

    // Time between consecutive hops / OSF samples: 256/44100 ≈ 5.805 ms.
    private const double HopMs = HopSize * 1000.0 / SampleRate;

    // Supported tempo range, shared by both estimators.
    private const double MinBpm = 15.0;
    private const double MaxBpm = 240.0;

    // Autocorrelation recompute cadence (off the audio thread).
    private const int TempoIntervalMs = 500;

    // Low-frequency band for kick/bass onset detection: 0–300 Hz.
    // At 44100/512 ≈ 86.1 Hz/bin this covers FFT bins 1, 2, 3 (~86, 172, 258 Hz).
    // LowBinHigh = ceil(300 * 512/44100) = 4, so the loop runs k = 1..3.
    private const double LowFreqMaxHz = 300.0;
    private static readonly int LowBinHigh =
        Math.Max(2, (int)Math.Ceiling(LowFreqMaxHz * FftSize / SampleRate));

    // Adaptive threshold: mean of the last FluxHistoryLen flux values × OnsetMultiplier.
    private const int FluxHistoryLen = 40;

    // Adaptive spectral-flux onset threshold: threshold = mean(flux history) × this.
    // Higher requires a more prominent transient to fire a beat; lower is more
    // sensitive. Fixed in code — intentionally not user-configurable.
    private const float OnsetMultiplier = 1.5f;

    // 200 ms minimum between onsets caps the visual pulse rate at 300 BPM and
    // suppresses double-triggers on the same transient across adjacent hops.
    private const double MinInterOnsetMs = 200.0;

    // ── Tempo output smoothing (TempoSmoother) ──────────────────────────────────
    // Fixed in code — intentionally not user-configurable (same convention as
    // OnsetMultiplier above). They tune the asymmetric confirmation filter that
    // rejects the transient upward spike during an input tempo change (OpenPoint 2):
    // a rise is "large" only when it clears BOTH the factor and the absolute floor,
    // and a large rise must persist for TempoUpConfirmCycles of the 500 ms tempo
    // cycle before it is adopted.
    //
    // The floor and confirm count are tuned against a real capture of a metronome
    // stepped 20→30 BPM (captures/osf-20260605-…): bumping the input tempo injects
    // one anomalously short transition interval (a 1.6 s gap → a spurious ~37 BPM
    // read) that the recency-weighted autocorrelation latches onto for ~one new beat
    // period before the true tempo fills the window. At 30 BPM that overshoot lasts
    // 4 cycles, so:
    //   • the floor must sit between the genuine step (+10 BPM here) and the overshoot
    //     (+17 BPM) so the real 30 BPM passes through immediately while the 37 is gated;
    //   • the confirm count must exceed the overshoot's 4 cycles so the settle-down
    //     reading (30) arrives and discards the pending 37 before it can confirm.
    // Cost: a *genuine* large up-jump is adopted ~1 s later (5 cycles vs 3). This is
    // the inherent latency/accuracy trade — the only signal that an overshoot is
    // spurious is that it falls back, which takes about one new beat period to observe.
    private const double TempoUpJumpFactor       = 1.25; // > +25 %  ⇒ candidate for gating
    private const int    TempoUpJumpMinBpm       = 14;   // and > +14 BPM (both required)
    private const int    TempoUpConfirmCycles    = 5;    // ≈ 2.5 s; outlasts the ~4-cycle overshoot
    private const int    TempoConfirmToleranceBpm = 8;   // successive candidates within this ⇒ same tempo

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

    // Peak-picking neighbourhood: the two most recent prior flux values. An onset
    // is only fired when the middle of three consecutive hops is a local maximum,
    // which suppresses the every-frame triggering that pinned dense music at the
    // 200 ms gate floor. _fluxPrev1 is one hop old, _fluxPrev2 two hops old.
    private double _fluxPrev1;
    private double _fluxPrev2;

    // Last beat TickCount64 (ms). 0 until the first beat is detected.
    private long _lastBeatTick;

    // True only while the capture service reports Running (signal above the −80 dBFS
    // silence floor). The service also publishes near-silent NoSignal frames for the
    // spectrum visualiser; gating onset firing on this flag stops the adaptive
    // threshold from collapsing on silence and emitting phantom beats (which would
    // pin BPM and keep the idle overlay hidden). Written from the capture thread via
    // StateChanged, read on the audio thread.
    private volatile bool _audioRunning;

    // ── Onset-strength envelope (OSF) for autocorrelation tempo estimation ───────
    // Continuous per-hop flux history (~OsfWindowSeconds). Written on the audio
    // thread, snapshot-copied by the tempo timer thread under _osfLock.
    private readonly double[] _osf;
    private readonly object   _osfLock = new();
    private int _osfWritePos;
    private int _osfCount;

    private readonly AutocorrelationTempoEstimator _tempoEstimator;
    private readonly double _sparsityMetronomeMin;
    private readonly double _sparsityDenseMax;

    // Smooths the published tempo: gates large upward jumps behind a few cycles of
    // confirmation so a transient change-spike never reaches CurrentBpm. Touched by
    // the tempo timer thread (Update) and the capture state thread (Reset); both are
    // internally locked. See TempoSmoother.
    private readonly TempoSmoother _tempoSmoother;

    // Diagnostic recorder for the OSF + per-cycle tempo output (OpenPoints.md item 2).
    // NullOsfCaptureSink unless --capture-osf is set, so this is zero-cost by default.
    private readonly IOsfCaptureSink _captureSink;

    // Total OSF hops appended since start, written only on the audio thread under _osfLock.
    // Snapshotted by the tempo timer as the capture's head index (see OnTempoTimer).
    private long _osfTotalHops;

    // Recomputes tempo every TempoIntervalMs off the audio thread so the O(lag×n)
    // autocorrelation never runs inside the <5 ms/hop audio budget.
    private readonly System.Threading.Timer _tempoTimer;

    // Tempo result, written by the tempo timer thread, read by UI threads.
    private volatile int   _autoBpm;
    private volatile float _autoConf;

    // Octave-fold hysteresis state. Touched only by the tempo timer thread.
    // Currently COMPUTED BUT NOT CONSUMED: octave folding is deliberately disabled
    // (see OnTempoTimer) while the autocorrelation estimator runs unfolded for all
    // input. The classifier is retained, not deleted, so folding can be re-enabled
    // later (e.g. behind a music/metronome UI toggle) without rebuilding it.
#pragma warning disable CS0414 // assigned for the retained classifier; intentionally unread for now
    private bool _foldDense;
#pragma warning restore CS0414

    // Tempo is estimated by autocorrelation of the onset-strength envelope in BOTH
    // regimes. Autocorrelation recovers the dominant period robustly even when the
    // discrete onset detector is noisy (e.g. a metronome tick carrying little low-
    // frequency energy, where IBI-from-onsets jitters wildly). The sparsity
    // classifier only decides whether to octave-fold (dense music) or not (sparse
    // metronome — preserving the full 15–240 BPM range). The discrete onsets now
    // feed only BeatDetected (the visual pulse / liveness), not the BPM value.
    public int   CurrentBpm => _autoBpm;
    // BPM estimation confidence — distinct from per-onset detection strength,
    // which is carried separately in BeatEventArgs.Confidence.
    public float Confidence => _autoConf;

    public event EventHandler<BeatEventArgs>? BeatDetected;

    public SpectralFluxBeatDetector(IAudioCaptureService audioService, AppSettings settings, IOsfCaptureSink captureSink)
    {
        _hannWindow      = BuildHannWindow(FftSize);

        int osfLen = Math.Max(64, (int)Math.Round(settings.OsfWindowSeconds * SampleRate / HopSize));
        _osf = new double[osfLen];

        _sparsityMetronomeMin = settings.SparsityMetronomeMin;
        _sparsityDenseMax     = settings.SparsityDenseMax;
        _tempoEstimator   = new AutocorrelationTempoEstimator(
            minBpm:            MinBpm,
            maxBpm:            MaxBpm,
            preferredCenter:   settings.PreferredBpmCenter,
            preferredSigma:    settings.PreferredBpmSigma,
            recencyTauSeconds: settings.RecencyTauSeconds);
        _tempoSmoother = new TempoSmoother(
            jumpUpFactor:        TempoUpJumpFactor,
            jumpUpMinBpm:        TempoUpJumpMinBpm,
            confirmCycles:       TempoUpConfirmCycles,
            confirmToleranceBpm: TempoConfirmToleranceBpm);

        _captureSink = captureSink;
        _captureSink.WriteHeader(new OsfCaptureHeader(
            HopMs:                    HopMs,
            SampleRate:               SampleRate,
            OsfLen:                   _osf.Length,
            OsfWindowSeconds:         settings.OsfWindowSeconds,
            MinBpm:                   MinBpm,
            MaxBpm:                   MaxBpm,
            PreferredCenter:          settings.PreferredBpmCenter,
            PreferredSigma:           settings.PreferredBpmSigma,
            RecencyTauSeconds:        settings.RecencyTauSeconds,
            SparsityMetronomeMin:     settings.SparsityMetronomeMin,
            SparsityDenseMax:         settings.SparsityDenseMax,
            TempoUpJumpFactor:        TempoUpJumpFactor,
            TempoUpJumpMinBpm:        TempoUpJumpMinBpm,
            TempoUpConfirmCycles:     TempoUpConfirmCycles,
            TempoConfirmToleranceBpm: TempoConfirmToleranceBpm));

        // Lifetime matches the app; unsubscribing / disposal is not needed.
        _audioRunning = audioService.State == AudioCaptureState.Running;
        audioService.SamplesAvailable += OnSamplesAvailable;
        audioService.StateChanged     += OnAudioStateChanged;
        _tempoTimer = new System.Threading.Timer(OnTempoTimer, null, TempoIntervalMs, TempoIntervalMs);
    }

    // Capture-thread callback. Tracks the running flag and, on a full stop, drops all
    // accumulated state so the readout does not linger on the last tempo after the
    // source goes away. NoSignal (a transient silent gap, e.g. a slow metronome) is
    // deliberately NOT reset — MaxIbiMs already bridges those gaps for the estimate.
    private void OnAudioStateChanged(object? sender, AudioCaptureState state)
    {
        _audioRunning = state == AudioCaptureState.Running;

        if (state is AudioCaptureState.Stopped or AudioCaptureState.Error)
        {
            _lastBeatTick = 0;
            _autoBpm      = 0;
            _autoConf     = 0f;
            _foldDense    = false;
            // Drop the smoother's baseline so the next session re-locks from scratch
            // rather than gating against a stale applied tempo.
            _tempoSmoother.Reset();
            lock (_osfLock)
            {
                _osfWritePos = 0;
                _osfCount    = 0;
            }
        }
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

        // Append flux to the continuous onset-strength envelope for the tempo timer.
        lock (_osfLock)
        {
            _osf[_osfWritePos] = flux;
            _osfWritePos = (_osfWritePos + 1) % _osf.Length;
            if (_osfCount < _osf.Length) _osfCount++;
            _osfTotalHops++;
            // Diagnostic capture: buffer-only, inside the lock so the recorder needs no
            // separate audio-thread synchronisation. No-op unless --capture-osf is set.
            _captureSink.RecordHop(flux, _audioRunning);
        }

        // Peak-picking: the onset candidate is the *previous* hop's flux, so we can
        // test it against both neighbours (one hop of look-ahead). Shift the window
        // after capturing the three values.
        double cand  = _fluxPrev1;
        double left  = _fluxPrev2;
        double right = flux;
        _fluxPrev2 = _fluxPrev1;
        _fluxPrev1 = flux;

        // Require minimal history (also guarantees prev1/prev2 are populated) to
        // avoid false positives during the initial fill period.
        if (_fluxHistoryCount < 3) return;

        // Do not declare onsets on silence/NoSignal frames: the signal is below the
        // −80 dBFS floor there, so any threshold crossing is noise. The OSF was still
        // appended above so the tempo analysis keeps an accurate picture of the gaps.
        if (!_audioRunning) return;

        double fluxMean = 0.0;
        for (int i = 0; i < _fluxHistoryCount; i++)
            fluxMean += _fluxHistory[i];
        fluxMean /= _fluxHistoryCount;

        double threshold = fluxMean * OnsetMultiplier;
        if (threshold <= 0.0) return;

        // Local-maximum test: only the peak of a rising-then-falling flux run fires,
        // not every frame that happens to sit above the adaptive threshold.
        bool isLocalMax = cand > left && cand >= right;
        if (!isLocalMax || cand <= threshold) return;

        // Min inter-onset interval guard (also caps the visual pulse rate).
        long   now     = Environment.TickCount64;
        double elapsed = _lastBeatTick == 0 ? double.MaxValue : now - _lastBeatTick;
        if (elapsed < MinInterOnsetMs) return;

        _lastBeatTick = now;

        // Onset confidence: how far above the adaptive threshold this peak was.
        // 0 = just at threshold, 1 = 2× threshold.
        float conf = (float)Math.Min(1.0, (cand / threshold - 1.0) * 2.0);

        BeatDetected?.Invoke(this, new BeatEventArgs(DateTimeOffset.UtcNow, conf));
    }

    // ── Tempo timer thread ──────────────────────────────────────────────────────

    // Runs every TempoIntervalMs on a ThreadPool thread. Snapshots the OSF, then
    // estimates tempo by autocorrelation. Sparsity decides only whether to octave-
    // fold: dense music is folded toward a musical range; a sparse click train is
    // left unfolded so it keeps its true tempo across the full 15–240 BPM range.
    private void OnTempoTimer(object? state)
    {
        double[] snapshot;
        long headIndex;
        lock (_osfLock)
        {
            // Need at least a quarter window before the estimate is meaningful.
            if (_osfCount < _osf.Length / 4)
                return;

            int size  = _osf.Length;
            int count = _osfCount;
            int start = count < size ? 0 : _osfWritePos;
            snapshot  = new double[count];
            for (int i = 0; i < count; i++)
                snapshot[i] = _osf[(start + i) % size];
            // Total hops appended so far: the capture's head index for this cycle, letting a
            // replay reconstruct the exact snapshot from the continuous OSF stream.
            headIndex = _osfTotalHops;
        }

        // OSF sparsity distinguishes a click train (mostly near-silent between clicks
        // ⇒ high, at any tempo and over a noise floor) from continuous music (envelope
        // stays elevated ⇒ low). Hysteresis band prevents regime flapping.
        double sparsity = ComputeSparsity(snapshot);

        if (sparsity <= _sparsityDenseMax)
            _foldDense = true;
        else if (sparsity >= _sparsityMetronomeMin)
            _foldDense = false;

        // Octave folding is deliberately DISABLED (fold: false), not driven by _foldDense.
        // The autocorrelation estimator's subharmonic-rejection step already recovers the
        // fundamental for accented metronomes that previously flapped between a tempo and
        // its divisors, and unfolded reporting preserves the true tempo across the full
        // 15–240 BPM range for both music and click trains. The sparsity classifier above
        // still runs so this can be re-enabled (pass fold: _foldDense) if a future music
        // mode wants octave folding back. See documentation/SoundModeImplementation.md §6.
        var est = _tempoEstimator.Analyze(snapshot, HopMs, fold: false);
        // Gate the published tempo: a large upward jump (e.g. the transient spike while
        // the input tempo changes) must be confirmed over a few cycles before it is
        // applied; decreases and small changes pass straight through. See TempoSmoother.
        _autoBpm  = _tempoSmoother.Update(est.Bpm);
        _autoConf = est.Confidence;

        // Diagnostic capture: records the raw (pre-smoother) estimate alongside the published
        // value, so a captured tempo spike can be replayed and attributed. No-op by default.
        _captureSink.RecordCycle(headIndex, est.Bpm, est.Confidence, sparsity, _autoBpm);
    }

    // Fraction of OSF hops that are near-silent: below 15 % of the 99th-percentile
    // peak. ~0.8–1.0 for a click train (long quiet gaps between clicks, at any tempo)
    // and ~0–0.2 for continuous music (envelope rarely drops near the floor). The
    // 99th percentile (rather than the raw max) is used as the peak reference so a
    // single outlier hop cannot distort the threshold. Runs off the audio thread.
    public static double ComputeSparsity(ReadOnlySpan<double> osf)
    {
        int n = osf.Length;
        if (n < 10) return 1.0;

        double[] sorted = osf.ToArray();
        Array.Sort(sorted);
        double peak = sorted[(int)(0.99 * (n - 1))];
        if (peak <= 1e-9) return 1.0; // silence → treat as sparse

        double threshold = 0.15 * peak;
        int below = 0;
        for (int i = 0; i < n; i++)
            if (osf[i] < threshold) below++;

        return (double)below / n;
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
