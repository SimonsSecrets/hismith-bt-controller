namespace HismithController.BeatDetection;

// Converts a stream of inter-beat intervals (IBIs) into a stable BPM estimate
// that also tracks tempo changes quickly.
//
// Design goals (see SoundModePlan.md §2.3):
//  - Support 15–240 BPM without a fixed time window.
//  - Re-lock within 2 beats on a hard tempo change.
//  - Reject single deviant beats as noise.
//
// Algorithm:
//  Stable state: maintain a ring buffer of the K most recent IBIs, report the
//  median (with light EMA smoothing once the estimate is stable).
//
//  Change detection: if a new IBI deviates >DeviationThreshold from the current
//  median, enter candidate state instead of adding it to the ring.
//    - If the NEXT IBI confirms the new tempo (within ConfirmationTolerance of
//      the candidate), flush the ring to just those two IBIs and report the new
//      BPM immediately — this caps change latency at 2 beats.
//    - If the NEXT IBI does not confirm, treat the deviant beat as noise, add
//      it to the ring, and keep the previous estimate.
//
// Threading: AddIbi is called on the NAudio audio thread. CurrentBpm and
// Confidence are read from the UI thread. Both fields are declared volatile so
// the UI sees the latest write without a lock.
public sealed class BpmEstimator
{
    // Sensible upper bound for K so the stack-allocated sort buffer is bounded.
    private const int MaxK = 16;

    private readonly int    _k;
    private readonly double _deviationThreshold;
    private readonly double _confirmTolerance;
    private readonly double _emaAlpha;

    // IBI ring buffer.  When _ibiCount < _k the valid entries are [0.._ibiCount);
    // when _ibiCount == _k the buffer is full and all _k entries are valid.
    private readonly double[] _ibis;
    private int _ibiPos;
    private int _ibiCount;

    private double _smoothedBpm;
    private int    _beatsSinceLastChange;

    // Candidate-change state.
    private bool   _inCandidateState;
    private double _candidateIbi;

    // volatile: written on the audio thread, read by UI / ViewModel threads.
    private volatile int   _currentBpm;
    private volatile float _confidence;

    public int   CurrentBpm => _currentBpm;
    public float Confidence => _confidence;

    public BpmEstimator(int k, double deviationThreshold, double confirmTolerance, double emaAlpha)
    {
        _k                  = Math.Clamp(k, 2, MaxK);
        _deviationThreshold = deviationThreshold;
        _confirmTolerance   = confirmTolerance;
        _emaAlpha           = emaAlpha;
        _ibis               = new double[_k];
    }

    // Add the next inter-beat interval (milliseconds) to the estimator.
    // Called on the audio thread; must not block.
    public void AddIbi(double ibiMs)
    {
        if (_inCandidateState)
            HandleConfirmation(ibiMs);
        else
            HandleStable(ibiMs);
    }

    // Reset all state — e.g. after a long audio gap where the accumulated IBIs
    // no longer reflect the current signal.
    public void Reset()
    {
        Array.Clear(_ibis, 0, _k);
        _ibiPos               = 0;
        _ibiCount             = 0;
        _smoothedBpm          = 0.0;
        _beatsSinceLastChange = 0;
        _inCandidateState     = false;
        _candidateIbi         = 0.0;
        _currentBpm           = 0;
        _confidence           = 0f;
    }

    // ── State machine ──────────────────────────────────────────────────────────

    private void HandleStable(double ibiMs)
    {
        if (_ibiCount >= 2)
        {
            double median    = ComputeMedian();
            double deviation = Math.Abs(ibiMs - median) / median;

            if (deviation > _deviationThreshold)
            {
                // Enter candidate state; don't add to ring yet.
                _inCandidateState = true;
                _candidateIbi     = ibiMs;
                // Confidence drops while we wait for the next beat to confirm.
                _confidence = 0.3f;
                return;
            }
        }

        AddToRing(ibiMs);
        // Increment before UpdateBpm so ComputeConfidence sees the post-beat count,
        // meaning BeatsSinceLastChange = K on the K-th stable beat → confidence 1.0.
        // Apply EMA only once the estimate has been stable for K beats so there is
        // no smoothing lag tail right after a tempo change.
        _beatsSinceLastChange++;
        UpdateBpm(smooth: _beatsSinceLastChange >= _k);
    }

    private void HandleConfirmation(double ibiMs)
    {
        double deviation = Math.Abs(ibiMs - _candidateIbi) / _candidateIbi;

        if (deviation <= _confirmTolerance)
        {
            // Confirmed: flush the ring to just the two agreeing IBIs and report
            // the new BPM immediately with no smoothing lag.
            Array.Clear(_ibis, 0, _k);
            _ibis[0]  = _candidateIbi;
            _ibis[1]  = ibiMs;
            _ibiPos   = 2 % _k;
            _ibiCount = Math.Min(2, _k);

            _beatsSinceLastChange = 0;
            _inCandidateState     = false;
            _candidateIbi         = 0.0;

            UpdateBpm(smooth: false);
        }
        else
        {
            // Not confirmed: the deviant beat was noise.  Add it to the ring so
            // the history stays accurate, then process the current IBI normally.
            double savedCandidate = _candidateIbi;
            _inCandidateState = false;
            _candidateIbi     = 0.0;

            AddToRing(savedCandidate);
            _beatsSinceLastChange++;
            UpdateBpm(smooth: _beatsSinceLastChange >= _k);

            HandleStable(ibiMs);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void AddToRing(double ibiMs)
    {
        _ibis[_ibiPos] = ibiMs;
        _ibiPos        = (_ibiPos + 1) % _k;
        if (_ibiCount < _k) _ibiCount++;
    }

    private void UpdateBpm(bool smooth)
    {
        if (_ibiCount < 2)
        {
            _currentBpm = 0;
            _confidence = 0f;
            return;
        }

        double medianMs = ComputeMedian();
        double rawBpm   = 60000.0 / medianMs;

        // EMA smoothing damps beat-to-beat jitter in the stable regime; skip it
        // during and immediately after a tempo change to avoid lag.
        double bpm = smooth && _smoothedBpm > 0.0
            ? _emaAlpha * rawBpm + (1.0 - _emaAlpha) * _smoothedBpm
            : rawBpm;

        _smoothedBpm = bpm;
        _currentBpm  = Math.Clamp((int)Math.Round(bpm), 15, 240);
        _confidence  = ComputeConfidence();
    }

    private float ComputeConfidence()
    {
        if (_ibiCount < 2) return 0f;
        if (_inCandidateState) return 0.3f;
        // Ramps 0.5 → 1.0 linearly as beats accumulate since the last change.
        // Reaches 1.0 after K consecutive stable beats.
        float stableRatio = Math.Min(1f, (float)_beatsSinceLastChange / _k);
        return 0.5f + 0.5f * stableRatio;
    }

    // Median of the _ibiCount valid ring-buffer values.
    // Median is preferred over mean because it tolerates a single outlier without
    // needing a percentile trim — which is numerically unstable at K=4.
    private double ComputeMedian()
    {
        // Stack-allocate a sort buffer (MaxK = 16 caps stack use to 128 bytes).
        Span<double> temp = stackalloc double[MaxK];
        int n = 0;
        for (int i = 0; i < _ibiCount; i++)
            temp[n++] = _ibis[i];

        // Insertion sort — O(K²) but K ≤ 16 makes this faster than Array.Sort
        // due to zero heap traffic on the hot audio thread.
        for (int i = 1; i < n; i++)
        {
            double key = temp[i];
            int    j   = i - 1;
            while (j >= 0 && temp[j] > key) { temp[j + 1] = temp[j]; j--; }
            temp[j + 1] = key;
        }

        return n % 2 == 0
            ? (temp[n / 2 - 1] + temp[n / 2]) / 2.0
            : temp[n / 2];
    }
}
