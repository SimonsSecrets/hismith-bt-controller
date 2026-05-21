using HismithController.BeatDetection;

namespace HismithController.Tests.BeatDetection;

// BpmEstimator is a pure state-machine — no I/O, no threads, no mocks needed.
// Tests feed synthetic IBIs and assert on CurrentBpm / Confidence.
public class BpmEstimatorTests
{
    // ── Factory ────────────────────────────────────────────────────────────────

    // Default parameters matching AppSettings defaults.
    private static BpmEstimator Default() =>
        new(k: 4, deviationThreshold: 0.25, confirmTolerance: 0.15, emaAlpha: 0.5);

    // Helper: feed n identical IBIs.
    private static void Feed(BpmEstimator est, double ibiMs, int count)
    {
        for (int i = 0; i < count; i++) est.AddIbi(ibiMs);
    }

    // ── Zero-state ────────────────────────────────────────────────────────────

    [Fact]
    public void CurrentBpm_BeforeAnyIbi_IsZero()
    {
        var est = Default();
        Assert.Equal(0, est.CurrentBpm);
    }

    [Fact]
    public void Confidence_BeforeAnyIbi_IsZero()
    {
        var est = Default();
        Assert.Equal(0f, est.Confidence);
    }

    [Fact]
    public void CurrentBpm_AfterOneIbi_IsZero()
    {
        // One IBI is not enough for a meaningful estimate.
        var est = Default();
        est.AddIbi(500.0);
        Assert.Equal(0, est.CurrentBpm);
    }

    // ── Stable convergence ────────────────────────────────────────────────────

    [Fact]
    public void CurrentBpm_AfterTwoIdenticalIbis_ReturnsCorrectBpm()
    {
        var est = Default();
        Feed(est, 500.0, 2); // 2 × 500 ms = 120 BPM
        Assert.Equal(120, est.CurrentBpm);
    }

    [Theory]
    [InlineData(500.0,  120)]   // 120 BPM
    [InlineData(600.0,  100)]   // 100 BPM
    [InlineData(857.14, 70)]    // 70 BPM (within rounding)
    [InlineData(1000.0, 60)]    //  60 BPM
    public void CurrentBpm_StableSignal_ConvergesCorrectly(double ibiMs, int expectedBpm)
    {
        var est = Default();
        Feed(est, ibiMs, 8);
        // Allow ±1 BPM for floating-point rounding.
        Assert.InRange(est.CurrentBpm, expectedBpm - 1, expectedBpm + 1);
    }

    // ── BPM range clamps ──────────────────────────────────────────────────────

    [Fact]
    public void LowBpm_15BpmMinimum()
    {
        var est = Default();
        Feed(est, 4000.0, 6); // 15 BPM
        Assert.Equal(15, est.CurrentBpm);
    }

    [Fact]
    public void HighBpm_240BpmMaximum()
    {
        var est = Default();
        Feed(est, 250.0, 6); // 240 BPM
        Assert.Equal(240, est.CurrentBpm);
    }

    [Fact]
    public void BpmAbove240_ClampedTo240()
    {
        // 200 ms IBIs → 300 BPM but clamped to 240.
        var est = Default();
        Feed(est, 200.0, 6);
        Assert.Equal(240, est.CurrentBpm);
    }

    [Fact]
    public void BpmBelow15_ClampedTo15()
    {
        // 10 s IBIs → 6 BPM but clamped to 15.
        var est = Default();
        Feed(est, 10_000.0, 6);
        Assert.Equal(15, est.CurrentBpm);
    }

    // ── Tempo change: confirmed within 2 beats ────────────────────────────────

    // The main correctness requirement from SoundModePlan §2.3:
    // "caps tempo-change latency at 2 beats at any tempo."

    [Fact]
    public void TempoChange_TwoConfirmingIbis_LocksNewBpmImmediately()
    {
        // Establish 120 BPM, then hard-switch to ~180 BPM.
        // IBIs: 500 ms → 500 ms → 500 ms → 500 ms → 500 ms → 500 ms
        //       then: 333 ms (candidate) → 333 ms (confirm) → expect ~180 BPM.
        var est = Default();
        Feed(est, 500.0, 6); // stable at 120 BPM
        Assert.InRange(est.CurrentBpm, 119, 121);

        // IBI 7: candidate (deviates 33 % from 500 ms median)
        est.AddIbi(333.0);
        // Still in candidate state — BPM should not have changed to 180 yet.
        // (It stays at the last reported value; the important thing is that we
        //  haven't locked in a wrong reading.)

        // IBI 8: confirmation (333 ms ≈ candidate; deviation ≈ 0 %)
        est.AddIbi(333.0);

        // After 2 beats the new tempo should be locked.
        Assert.InRange(est.CurrentBpm, 178, 182);
    }

    [Fact]
    public void TempoChange_60To120Bpm_LocksWithinTwoBeats()
    {
        var est = Default();
        Feed(est, 1000.0, 6); // stable at 60 BPM
        Assert.InRange(est.CurrentBpm, 59, 61);

        est.AddIbi(500.0); // candidate (50 % deviation)
        est.AddIbi(500.0); // confirmation

        Assert.InRange(est.CurrentBpm, 119, 121);
    }

    [Fact]
    public void TempoChange_120To60Bpm_LocksWithinTwoBeats()
    {
        var est = Default();
        Feed(est, 500.0, 6); // stable at 120 BPM

        est.AddIbi(1000.0); // candidate
        est.AddIbi(1000.0); // confirmation

        Assert.InRange(est.CurrentBpm, 59, 61);
    }

    // ── Noise rejection ───────────────────────────────────────────────────────

    [Fact]
    public void NoiseBeat_NotConfirmed_KeepsOriginalBpm()
    {
        // One deviant beat followed by a return to the original IBI should be
        // treated as noise; the BPM should not permanently shift.
        var est = Default();
        Feed(est, 500.0, 6); // 120 BPM

        est.AddIbi(800.0); // candidate (60 % deviation)
        est.AddIbi(500.0); // does NOT confirm 800 ms → noise rejected

        // Median of [500, 500, 500, 800] = (500+500)/2 = 500 ms → 120 BPM.
        Assert.InRange(est.CurrentBpm, 118, 122);
    }

    [Fact]
    public void NoiseBeat_Rejected_ConfidenceRecovers()
    {
        var est = Default();
        Feed(est, 500.0, 8); // well-established 120 BPM

        est.AddIbi(800.0); // candidate — confidence drops
        Assert.True(est.Confidence < 0.5f, "Confidence should drop in candidate state.");

        est.AddIbi(500.0); // rejection — BPM stays, confidence begins recovering
        // After rejection the estimator is back in stable mode.
        Assert.True(est.Confidence >= 0.3f, "Confidence should have started recovering.");
    }

    // ── Confidence progression ────────────────────────────────────────────────

    [Fact]
    public void Confidence_RisesAfterTempoChange()
    {
        var est = Default();
        Feed(est, 500.0, 6);

        est.AddIbi(333.0);  // candidate
        est.AddIbi(333.0);  // confirmation → BeatsSinceLastChange reset to 0

        float confAtChange = est.Confidence;
        Assert.InRange(confAtChange, 0.49f, 0.51f); // should be exactly 0.5

        // Each subsequent stable beat should raise confidence.
        float prev = confAtChange;
        for (int i = 0; i < 4; i++)
        {
            est.AddIbi(333.0);
            Assert.True(est.Confidence >= prev,
                $"Confidence should not decrease on beat {i + 1} after change.");
            prev = est.Confidence;
        }

        Assert.InRange(est.Confidence, 0.99f, 1.01f); // fully recovered after K beats
    }

    [Fact]
    public void Confidence_DropsDuringCandidateState()
    {
        var est = Default();
        Feed(est, 500.0, 6);
        float stableConf = est.Confidence;

        est.AddIbi(800.0); // enter candidate state

        Assert.True(est.Confidence < stableConf,
            "Confidence must drop when a change candidate is pending.");
        Assert.InRange(est.Confidence, 0.29f, 0.31f);
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsAllState()
    {
        var est = Default();
        Feed(est, 500.0, 8);
        Assert.NotEqual(0, est.CurrentBpm);

        est.Reset();

        Assert.Equal(0, est.CurrentBpm);
        Assert.Equal(0f, est.Confidence);
    }

    [Fact]
    public void Reset_ThenAddIbis_ConvergesCorrectly()
    {
        var est = Default();
        Feed(est, 500.0, 8);
        est.Reset();
        Feed(est, 1000.0, 6); // 60 BPM after reset
        Assert.InRange(est.CurrentBpm, 59, 61);
    }

    // ── Metronome sweep: the §2.3 "every 5 seconds" scenario ──────────────────

    [Fact]
    public void MetronomeSweep_TracksEachTempoWithinTwoBeats()
    {
        // Simulate a metronome that cycles through 60 → 120 → 90 → 180 BPM.
        // At each tempo we feed 3 "warm-up" beats so the old tempo is established,
        // then 2 beats at the new tempo and verify the estimator has locked on.
        var est = Default();

        int[] bpms     = [60, 120, 90, 180];
        int   previous = bpms[0];
        Feed(est, 60_000.0 / previous, 6); // warm up at first tempo

        for (int i = 1; i < bpms.Length; i++)
        {
            int    next  = bpms[i];
            double ibiMs = 60_000.0 / next;

            // First beat at new tempo: candidate
            est.AddIbi(ibiMs);
            // Second beat at new tempo: confirmation
            est.AddIbi(ibiMs);

            Assert.InRange(est.CurrentBpm, next - 3, next + 3);

            // Warm-up for the next transition.
            Feed(est, ibiMs, 4);
            previous = next;
        }
    }

    // ── K=2 edge case ─────────────────────────────────────────────────────────

    [Fact]
    public void K2_MinimumWindow_ConvergesImmediately()
    {
        var est = new BpmEstimator(k: 2, deviationThreshold: 0.25,
                                   confirmTolerance: 0.15, emaAlpha: 0.5);
        est.AddIbi(500.0);
        est.AddIbi(500.0);
        Assert.InRange(est.CurrentBpm, 119, 121);
    }

    // ── Large K ───────────────────────────────────────────────────────────────

    [Fact]
    public void K8_LargerWindow_StillConverges()
    {
        var est = new BpmEstimator(k: 8, deviationThreshold: 0.25,
                                   confirmTolerance: 0.15, emaAlpha: 0.5);
        Feed(est, 500.0, 10);
        Assert.InRange(est.CurrentBpm, 119, 121);
    }
}
