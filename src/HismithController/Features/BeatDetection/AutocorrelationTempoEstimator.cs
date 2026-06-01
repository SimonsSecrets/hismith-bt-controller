namespace HismithController.BeatDetection;

// Estimates tempo from the *periodicity* of a continuous onset-strength envelope
// (OSF) via autocorrelation, rather than from discrete inter-beat intervals.
//
// Why this exists: the IBI-median BpmEstimator assumes every detected onset is a
// beat. That holds for a metronome but not for real music, where onsets land on
// many metrical subdivisions (eighths, sixteenths, syncopation) plus noise. For
// such dense content the only reliable tempo cue is the dominant period of the
// onset envelope, which autocorrelation recovers regardless of which subdivisions
// produced the onsets.
//
// Octave handling: autocorrelation peaks at the true period AND its integer
// multiples/divisors, so a 120 BPM song also peaks at 60 and 240. Analyze folds
// the candidate harmonic set toward a musical range using a log-BPM Gaussian
// preference, resolving half/double-tempo without hard-capping.
//
// Pure and stateless apart from the last result — no threads, no I/O — so it is
// unit-testable by feeding synthetic OSF arrays (see AutocorrelationTempoEstimatorTests).
public sealed class AutocorrelationTempoEstimator
{
    public readonly record struct TempoEstimate(int Bpm, float Confidence);

    private readonly double _minBpm;
    private readonly double _maxBpm;
    private readonly double _preferredCenter;
    private readonly double _preferredSigma;

    public AutocorrelationTempoEstimator(
        double minBpm,
        double maxBpm,
        double preferredCenter,
        double preferredSigma)
    {
        _minBpm          = minBpm;
        _maxBpm          = maxBpm;
        _preferredCenter = preferredCenter;
        _preferredSigma  = preferredSigma;
    }

    // Estimate tempo from an OSF snapshot. hopMs is the time between OSF samples.
    // When fold is true the dominant peak is octave-folded toward the preferred
    // tempo range; when false the dominant peak is reported as-is.
    public TempoEstimate Analyze(ReadOnlySpan<double> osf, double hopMs, bool fold = true)
    {
        int n = osf.Length;
        if (n < 8 || hopMs <= 0.0)
            return new TempoEstimate(0, 0f);

        // Lag bounds (in OSF samples) for the supported BPM range. A faster tempo
        // ⇒ shorter period ⇒ smaller lag. Cap lagMax at n/2 so every lag still has
        // at least half the window of overlapping terms.
        int lagMin = (int)Math.Floor(60_000.0 / (_maxBpm * hopMs));
        int lagMax = (int)Math.Ceiling(60_000.0 / (_minBpm * hopMs));
        lagMin = Math.Max(1, lagMin);
        lagMax = Math.Min(lagMax, n / 2);
        if (lagMax <= lagMin)
            return new TempoEstimate(0, 0f);

        double mean = 0.0;
        for (int i = 0; i < n; i++) mean += osf[i];
        mean /= n;

        // Total squared deviation (= lag-0 autocorrelation) normalises the result
        // into ~[-1, 1]. A flat/silent envelope has ~zero deviation → no tempo.
        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            double d = osf[i] - mean;
            sumSq += d * d;
        }
        if (sumSq <= 1e-12)
            return new TempoEstimate(0, 0f);

        // Biased autocorrelation: every lag is divided by the same constant (sumSq),
        // not by its overlap count. This intentionally favours shorter lags, so the
        // picked peak is the fundamental or a *higher* harmonic of the true tempo —
        // never a subharmonic. The octave-fold stage then divides that down into the
        // musical range. (Per-count normalisation would inflate slow lags and could
        // lock onto a half-tempo subharmonic the fold cannot recover.)
        Span<double> ac = lagMax + 1 <= 1024
            ? stackalloc double[lagMax + 1]
            : new double[lagMax + 1];

        int bestLag = lagMin;
        double bestAc = double.NegativeInfinity;
        for (int lag = lagMin; lag <= lagMax; lag++)
        {
            double sum = 0.0;
            int    cnt = n - lag;
            for (int i = 0; i < cnt; i++)
                sum += (osf[i] - mean) * (osf[i + lag] - mean);

            double norm = sum / sumSq;
            ac[lag] = norm;

            if (norm > bestAc)
            {
                bestAc  = norm;
                bestLag = lag;
            }
        }

        if (bestAc <= 0.0)
            return new TempoEstimate(0, 0f);

        // Parabolic interpolation around the peak for sub-sample lag precision,
        // which matters at small lags where one sample is several BPM.
        double refinedLag = bestLag;
        if (bestLag > lagMin && bestLag < lagMax)
        {
            double ym1 = ac[bestLag - 1];
            double y0  = ac[bestLag];
            double yp1 = ac[bestLag + 1];
            double denom = ym1 - 2.0 * y0 + yp1;
            if (Math.Abs(denom) > 1e-12)
                refinedLag = bestLag + 0.5 * (ym1 - yp1) / denom;
        }

        double bpm = 60_000.0 / (refinedLag * hopMs);

        if (fold)
            bpm = FoldToPreferredOctave(bpm, ac, hopMs, lagMin, lagMax);

        bpm = Math.Clamp(bpm, _minBpm, _maxBpm);

        // Confidence: the normalised autocorrelation at the dominant peak is already
        // a correlation strength in [0, 1] for a well-locked tempo.
        float confidence = (float)Math.Clamp(bestAc, 0.0, 1.0);

        return new TempoEstimate((int)Math.Round(bpm), confidence);
    }

    // Choose among the harmonic set {bpm×2, bpm, bpm/2, bpm/3} the candidate that
    // maximises autocorrelation strength × preference weight, keeping only those
    // inside [_minBpm, _maxBpm]. This collapses double/half-tempo readings toward
    // the musical range without a hard cap.
    private double FoldToPreferredOctave(
        double bpm, ReadOnlySpan<double> ac, double hopMs, int lagMin, int lagMax)
    {
        ReadOnlySpan<double> factors = stackalloc double[] { 2.0, 1.0, 0.5, 1.0 / 3.0 };

        double bestBpm   = bpm;
        double bestScore = double.NegativeInfinity;
        foreach (double f in factors)
        {
            double candBpm = bpm * f;
            if (candBpm < _minBpm || candBpm > _maxBpm)
                continue;

            double candLag = 60_000.0 / (candBpm * hopMs);
            double strength = InterpolateAc(ac, candLag, lagMin, lagMax);
            if (strength <= 0.0)
                continue;

            double score = strength * Preference(candBpm);
            if (score > bestScore)
            {
                bestScore = score;
                bestBpm   = candBpm;
            }
        }

        return bestBpm;
    }

    // Gaussian in log2(BPM): peaks at _preferredCenter, _preferredSigma in octaves.
    private double Preference(double bpm)
    {
        double octaves = Math.Log2(bpm / _preferredCenter) / _preferredSigma;
        return Math.Exp(-0.5 * octaves * octaves);
    }

    private static double InterpolateAc(
        ReadOnlySpan<double> ac, double lag, int lagMin, int lagMax)
    {
        if (lag <= lagMin) return ac[lagMin];
        if (lag >= lagMax) return ac[lagMax];

        int    lo = (int)Math.Floor(lag);
        double t  = lag - lo;
        return ac[lo] * (1.0 - t) + ac[lo + 1] * t;
    }
}
