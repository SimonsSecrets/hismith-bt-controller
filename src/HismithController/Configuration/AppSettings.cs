namespace HismithController.Configuration;

public sealed class AppSettings
{
    public bool UseMockBle { get; set; }
    public bool UseMockAudio { get; set; }

    // When set (via --capture-osf), the beat detector records its onset-strength envelope and
    // per-cycle tempo output to this text file for offline replay (OpenPoints.md item 2). Null
    // ⇒ capture disabled. See IOsfCaptureSink / OsfFileCaptureSink and tools/OsfReplay.
    public string? OsfCapturePath { get; set; }

    // ── Autocorrelation tempo estimator ─────────────────────────────────────────
    // Tempo is derived from the periodicity of the onset-strength envelope via
    // autocorrelation, not from discrete inter-beat intervals: real music places
    // onsets on many metrical subdivisions, so an interval-based estimate is
    // unreliable. See AutocorrelationTempoEstimator.cs.

    // Length of the onset-strength-envelope history (seconds) fed to autocorrelation.
    // Must span several beats at the slowest tempo; 6 s covers ~1.5 beats at 15 BPM
    // and many beats at fast tempos, trading latency for a stable estimate.
    public double OsfWindowSeconds { get; set; } = 6.0;

    // Recency weighting (seconds): the autocorrelation is computed over an
    // exponentially recency-weighted copy of the OSF with this time constant, so a
    // tempo change is not masked by stale old-tempo evidence still in the 6 s window.
    // Lower ⇒ snappier reaction to tempo changes but noisier; higher ⇒ steadier but
    // slower. ~2.5 s reacts to a high→low change in ~2–3 s (vs ~6 s) while keeping the
    // full window so slow tempos still lock. Set to 0 to disable (uniform weighting,
    // identical to the original behaviour).
    public double RecencyTauSeconds { get; set; } = 2.5;

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
