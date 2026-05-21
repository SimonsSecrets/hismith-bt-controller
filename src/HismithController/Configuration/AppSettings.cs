namespace HismithController.Configuration;

public sealed class AppSettings
{
    public bool UseMockBle { get; set; }
    public bool UseMockAudio { get; set; }

    // Onset multiplier for adaptive spectral-flux threshold: threshold = mean(flux history) × value.
    // Higher values require a more prominent transient to trigger a beat; lower values are more sensitive.
    public double OnsetMultiplier { get; set; } = 1.5;

    // BPM estimator tuning — see BpmEstimator.cs for the full algorithm description.
    // K: number of inter-beat intervals kept in the ring buffer.
    //    Larger K = smoother but slower to converge; smaller = faster but noisier.
    public int BpmWindowK { get; set; } = 4;

    // Fraction of the current median IBI that a new IBI must deviate by before
    // being treated as a tempo-change candidate (rather than added to the ring).
    // 0.25 = 25 % deviation triggers candidate state.
    public double BpmDeviationThreshold { get; set; } = 0.25;

    // Fraction of the candidate IBI that the following IBI must be within to
    // confirm the tempo change and flush the ring buffer.
    public double BpmConfirmationTolerance { get; set; } = 0.15;

    // EMA smoothing factor applied in the stable regime (BeatsSinceLastChange ≥ K).
    // 0.5 weights the new raw BPM and the previous smoothed BPM equally.
    public double BpmEmaAlpha { get; set; } = 0.5;
}
