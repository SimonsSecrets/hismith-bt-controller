using HismithController.BeatDetection;

namespace HismithController.Tests.BeatDetection;

// TempoSmoother is the pure asymmetric confirmation filter on the published tempo:
// decreases and small increases pass straight through, but a large upward jump must
// persist for ConfirmCycles consecutive cycles before it is adopted — so the transient
// spike during an input tempo change (OpenPoint 2) never reaches CurrentBpm.
public class TempoSmootherTests
{
    // Same constants the detector uses: large = > +25 % AND > +14 BPM; 5 cycles to confirm.
    // corroborationThreshold defaults to 1.0 (early adoption disabled) so the time-based
    // path is exercised in isolation; the corroboration tests construct their own.
    private static TempoSmoother NewSmoother()
        => new(jumpUpFactor: 1.25, jumpUpMinBpm: 14, confirmCycles: 5, confirmToleranceBpm: 8);

    // Detector config with corroboration enabled (matches TempoCorroborationMin = 0.25).
    private static TempoSmoother NewCorroboratingSmoother()
        => new(jumpUpFactor: 1.25, jumpUpMinBpm: 14, confirmCycles: 5, confirmToleranceBpm: 8,
               corroborationThreshold: 0.25);

    [Fact]
    public void FirstReading_AdoptedImmediately()
        => Assert.Equal(100, NewSmoother().Update(100));

    [Fact]
    public void Decrease_AdoptedImmediately()
    {
        var s = NewSmoother();
        s.Update(120);
        Assert.Equal(60, s.Update(60));
    }

    [Fact]
    public void SmallIncrease_AdoptedImmediately()
    {
        var s = NewSmoother();
        s.Update(100);
        // 110 is only +10 % and +10 BPM — under both thresholds, so not gated.
        Assert.Equal(110, s.Update(110));
    }

    [Fact]
    public void SmallAbsoluteJumpAtLowBpm_AdoptedImmediately()
    {
        var s = NewSmoother();
        s.Update(30);
        // 40 clears the +25 % factor but not the +14 BPM floor → not "large".
        Assert.Equal(40, s.Update(40));
    }

    [Fact]
    public void SmallRelativeWobbleAtHighBpm_AdoptedImmediately()
    {
        var s = NewSmoother();
        s.Update(200);
        // 225 clears the +20 BPM floor but not the +25 % factor (250) → not "large".
        Assert.Equal(225, s.Update(225));
    }

    [Fact]
    public void LargeUpJump_HeldUntilConfirmed()
    {
        var s = NewSmoother();
        s.Update(100);
        Assert.Equal(100, s.Update(200)); // cycle 1 — pending, output unchanged
        Assert.Equal(100, s.Update(200)); // cycle 2 — still pending
        Assert.Equal(100, s.Update(200)); // cycle 3 — still pending
        Assert.Equal(100, s.Update(200)); // cycle 4 — still pending
        Assert.Equal(200, s.Update(200)); // cycle 5 — confirmed, adopted
    }

    [Fact]
    public void TransientSpike_Rejected()
    {
        var s = NewSmoother();
        s.Update(100);
        Assert.Equal(100, s.Update(210)); // spike begins
        Assert.Equal(100, s.Update(210)); // still only 2 cycles — below ConfirmCycles
        Assert.Equal(100, s.Update(100)); // spike gone → baseline retained, pending dropped
    }

    // Regression for capture osf-20260605-125501.txt: a metronome stepped 20→30 BPM injects
    // one short transition interval that the estimator briefly reads as ~37 BPM for ~4 cycles
    // before the true 30 fills the window. The +14 floor gates the 37 (it is +17 over 20) but
    // lets the genuine 30 (+10) through, and 5-cycle confirmation outlasts the overshoot so 30
    // arrives and discards the pending 37 — the overshoot must never reach the output.
    [Fact]
    public void MetronomeStepOvershoot_NeverAdopted()
    {
        var s = NewSmoother();
        Assert.Equal(20, s.Update(20));
        Assert.Equal(20, s.Update(37)); // overshoot cycle 1 — gated, held
        Assert.Equal(20, s.Update(37)); // cycle 2
        Assert.Equal(20, s.Update(37)); // cycle 3
        Assert.Equal(20, s.Update(37)); // cycle 4 — still under ConfirmCycles
        Assert.Equal(30, s.Update(30)); // settle: +10 over 20 is not "large" → adopted, 37 dropped
    }

    [Fact]
    public void CorroboratedLargeUpJump_AdoptedImmediately()
    {
        var s = NewCorroboratingSmoother();
        s.Update(120);
        // 180 is a large up-jump (+50 % / +60 BPM); support 0.7 ≥ 0.25 ⇒ the new tempo
        // already repeats, so it is adopted at once instead of waiting 5 cycles.
        Assert.Equal(180, s.Update(180, harmonicSupport: 0.7));
    }

    [Fact]
    public void UncorroboratedLargeUpJump_StillWaitsConfirmCycles()
    {
        var s = NewCorroboratingSmoother();
        s.Update(120);
        // Support 0.1 < 0.25: not corroborated, so the time-based gate still applies.
        Assert.Equal(120, s.Update(180, harmonicSupport: 0.1));
        Assert.Equal(120, s.Update(180, harmonicSupport: 0.1));
        Assert.Equal(120, s.Update(180, harmonicSupport: 0.1));
        Assert.Equal(120, s.Update(180, harmonicSupport: 0.1));
        Assert.Equal(180, s.Update(180, harmonicSupport: 0.1)); // 5th cycle
    }

    [Fact]
    public void CorroborationMidPending_AdoptsImmediately()
    {
        var s = NewCorroboratingSmoother();
        s.Update(120);
        Assert.Equal(120, s.Update(180, harmonicSupport: 0.1)); // pending, not yet corroborated
        Assert.Equal(120, s.Update(180, harmonicSupport: 0.1));
        // Corroboration appears (the new period has now repeated) → adopt at once.
        Assert.Equal(180, s.Update(180, harmonicSupport: 0.6));
    }

    [Fact]
    public void Uncorroborated37Overshoot_StillRejected()
    {
        // The transition gap has ~zero harmonic support, so corroboration never fires and
        // the +14 floor / 5-cycle gate still discards it (regression guard with early-adopt on).
        var s = NewCorroboratingSmoother();
        Assert.Equal(20, s.Update(20, harmonicSupport: 0.0));
        Assert.Equal(20, s.Update(37, harmonicSupport: 0.0));
        Assert.Equal(20, s.Update(37, harmonicSupport: 0.0));
        Assert.Equal(20, s.Update(37, harmonicSupport: 0.0));
        Assert.Equal(20, s.Update(37, harmonicSupport: 0.0));
        Assert.Equal(30, s.Update(30, harmonicSupport: 0.0)); // settle: +10 not large → adopted, 37 dropped
    }

    [Fact]
    public void CandidateOutsideTolerance_RestartsConfirmation()
    {
        var s = NewSmoother();
        s.Update(100);
        Assert.Equal(100, s.Update(200)); // pending 200, count 1
        Assert.Equal(100, s.Update(220)); // |220-200| = 20 > 8 → new candidate, count 1
        Assert.Equal(100, s.Update(220)); // count 2
        Assert.Equal(100, s.Update(220)); // count 3
        Assert.Equal(100, s.Update(220)); // count 4
        Assert.Equal(220, s.Update(220)); // count 5 → adopted
    }

    [Fact]
    public void Zero_PassesThroughAndClearsPending()
    {
        var s = NewSmoother();
        s.Update(100);
        s.Update(200);             // pending up-jump in flight
        Assert.Equal(0, s.Update(0));
        // After 0 the baseline is cleared, so the next reading is adopted immediately.
        Assert.Equal(200, s.Update(200));
    }

    [Fact]
    public void Reset_DropsPendingAndRebaselines()
    {
        var s = NewSmoother();
        s.Update(100);
        s.Update(200);             // pending up-jump, not yet adopted
        s.Reset();
        // Baseline gone → the large value is taken as the fresh baseline immediately.
        Assert.Equal(200, s.Update(200));
    }
}
