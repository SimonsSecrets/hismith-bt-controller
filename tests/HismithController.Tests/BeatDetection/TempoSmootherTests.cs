using HismithController.BeatDetection;

namespace HismithController.Tests.BeatDetection;

// TempoSmoother is the pure asymmetric confirmation filter on the published tempo:
// decreases and small increases pass straight through, but a large upward jump must
// persist for ConfirmCycles consecutive cycles before it is adopted — so the transient
// spike during an input tempo change (OpenPoint 2) never reaches CurrentBpm.
public class TempoSmootherTests
{
    // Same constants the detector uses: large = > +25 % AND > +20 BPM; 3 cycles to confirm.
    private static TempoSmoother NewSmoother()
        => new(jumpUpFactor: 1.25, jumpUpMinBpm: 20, confirmCycles: 3, confirmToleranceBpm: 8);

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
        // 40 clears the +25 % factor but not the +20 BPM floor → not "large".
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
        Assert.Equal(200, s.Update(200)); // cycle 3 — confirmed, adopted
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

    [Fact]
    public void CandidateOutsideTolerance_RestartsConfirmation()
    {
        var s = NewSmoother();
        s.Update(100);
        Assert.Equal(100, s.Update(200)); // pending 200, count 1
        Assert.Equal(100, s.Update(220)); // |220-200| = 20 > 8 → new candidate, count 1
        Assert.Equal(100, s.Update(220)); // count 2
        Assert.Equal(220, s.Update(220)); // count 3 → adopted
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
