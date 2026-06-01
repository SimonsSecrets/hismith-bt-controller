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

    // ── Autocorrelation tempo estimator (dense/song regime) ─────────────────────
    // The IBI-median estimator above assumes every detected onset is a beat, which
    // holds for a metronome but not for real music (onsets land on many metrical
    // subdivisions). For dense content the periodicity of the onset-strength
    // envelope is estimated by autocorrelation instead. See AutocorrelationTempoEstimator.cs.

    // Length of the onset-strength-envelope history (seconds) fed to autocorrelation.
    // Must span several beats at the slowest tempo; 6 s covers ~1.5 beats at 15 BPM
    // and many beats at fast tempos, trading latency for a stable estimate.
    public double OsfWindowSeconds { get; set; } = 6.0;

    // Regime classifier: OSF sparsity = the fraction of hops that are near-silent
    // (below 15 % of the envelope's 99th-percentile peak). A click train (metronome)
    // spends most of its time near the floor between clicks ⇒ high sparsity at ANY
    // tempo, even over a steady noise floor; continuous music keeps the envelope
    // elevated ⇒ low sparsity. This is tempo-robust (unlike an energy-concentration
    // measure, which collapses for fast click trains) and noise-floor-robust (unlike
    // "fraction above the mean", which mis-routed metronomes to the folding path).
    // The two bounds form a hysteresis band so the regime cannot flap.
    public double SparsityMetronomeMin { get; set; } = 0.60; // ≥ ⇒ sparse/metronome
    public double SparsityDenseMax     { get; set; } = 0.40; // ≤ ⇒ dense/song

    // Preferred-tempo weighting for octave folding (dense regime only): a Gaussian
    // in log2(BPM) space centred on PreferredBpmCenter with width PreferredBpmSigma
    // (in octaves). Resolves half/double-tempo ambiguity toward a musical range
    // (~60–180 BPM) without hard-capping. Not applied in the sparse/metronome regime.
    public double PreferredBpmCenter { get; set; } = 120.0;
    public double PreferredBpmSigma  { get; set; } = 0.5;
}
