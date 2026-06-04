namespace HismithController.BeatDetection;

// Asymmetric confirmation filter on the tempo estimate, sitting between the
// autocorrelation result and the published CurrentBpm. Its job is to reject the
// brief upward spike that the recency-weighted autocorrelation produces while the
// input tempo is changing (a decaying old period plus a couple of close transition
// ticks momentarily latch a spurious short lag). See OpenPoints.md item 2 and
// SoundModeImplementation.md.
//
// Rules, applied once per tempo cycle (the detector's 500 ms timer):
//   • First non-zero reading after start/reset  → adopt immediately.
//   • Reading of 0 (lock lost / silence)         → pass through 0, clear pending.
//   • Decrease or small increase                 → adopt immediately (responsive;
//                                                   slowing must never be delayed).
//   • Large upward jump                          → hold as a pending candidate;
//                                                   adopt only after it persists for
//                                                   ConfirmCycles consecutive cycles.
// A spike that fades before ConfirmCycles is therefore discarded and the output
// holds the previous tempo steady.
//
// Threading: Update runs on the tempo-timer thread; Reset runs on the audio capture
// state-change thread. Both are guarded by _gate so a Reset cannot interleave with
// an Update and leave half-updated state. Both calls are infrequent (≤ one per
// 500 ms / per state change), so the lock cost is irrelevant.
public sealed class TempoSmoother
{
    private readonly double _jumpUpFactor;
    private readonly int    _jumpUpMinBpm;
    private readonly int    _confirmCycles;
    private readonly int    _confirmToleranceBpm;

    private readonly object _gate = new();

    // Currently published tempo. 0 means "nothing adopted yet" (start / after reset).
    private int _applied;
    // Candidate large up-jump awaiting confirmation, and how many consecutive cycles
    // it has held. _pendingCount == 0 means there is no candidate in flight.
    private int _pending;
    private int _pendingCount;

    // jumpUpFactor / jumpUpMinBpm: a rise counts as "large" only when it clears BOTH a
    // relative factor and an absolute floor. Requiring both avoids gating tiny absolute
    // drift at low BPM (the factor alone would) and small relative wobble at high BPM
    // (the floor alone would). confirmCycles: how many consecutive cycles a large jump
    // must persist before it is adopted. confirmToleranceBpm: how close successive
    // candidates must be to count as confirming the same pending tempo.
    public TempoSmoother(double jumpUpFactor, int jumpUpMinBpm, int confirmCycles, int confirmToleranceBpm)
    {
        _jumpUpFactor        = jumpUpFactor;
        _jumpUpMinBpm        = jumpUpMinBpm;
        _confirmCycles       = Math.Max(1, confirmCycles);
        _confirmToleranceBpm = confirmToleranceBpm;
    }

    // Feed one raw tempo reading; returns the tempo to publish this cycle.
    public int Update(int rawBpm)
    {
        lock (_gate)
        {
            // Lost lock / silence: drop to 0 and forget any pending candidate so the
            // next real reading re-locks immediately rather than against a stale base.
            if (rawBpm <= 0)
            {
                _applied      = 0;
                _pending      = 0;
                _pendingCount = 0;
                return 0;
            }

            // Nothing adopted yet → take the first reading as the baseline.
            if (_applied == 0)
            {
                _applied      = rawBpm;
                _pending      = 0;
                _pendingCount = 0;
                return _applied;
            }

            bool largeJumpUp = rawBpm > _applied * _jumpUpFactor
                            && rawBpm > _applied + _jumpUpMinBpm;

            // Decrease or small increase → adopt now and discard any pending up-jump
            // (the input clearly is not climbing to that candidate).
            if (!largeJumpUp)
            {
                _applied      = rawBpm;
                _pending      = 0;
                _pendingCount = 0;
                return _applied;
            }

            // Large upward jump: require confirmation across consecutive cycles. A
            // reading near the existing candidate extends its streak; otherwise it
            // starts a fresh candidate at count 1.
            if (_pendingCount > 0 && Math.Abs(rawBpm - _pending) <= _confirmToleranceBpm)
                _pendingCount++;
            else
            {
                _pending      = rawBpm;
                _pendingCount = 1;
            }

            if (_pendingCount >= _confirmCycles)
            {
                _applied      = _pending;
                _pending      = 0;
                _pendingCount = 0;
            }

            return _applied;
        }
    }

    // Drops all state, including an in-progress pending candidate, so the next reading
    // is adopted as a fresh baseline. Called when capture stops/errors.
    public void Reset()
    {
        lock (_gate)
        {
            _applied      = 0;
            _pending      = 0;
            _pendingCount = 0;
        }
    }
}
