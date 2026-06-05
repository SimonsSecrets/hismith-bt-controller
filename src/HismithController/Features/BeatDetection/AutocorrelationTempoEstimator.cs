namespace HismithController.BeatDetection;

// Estimates tempo from the *periodicity* of a continuous onset-strength envelope
// (OSF) via autocorrelation, rather than from discrete inter-beat intervals.
//
// Why this exists: an inter-beat-interval (IBI) median assumes every detected
// onset is a beat. That holds for a metronome but not for real music, where onsets land on
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
// Recency weighting: the autocorrelation is computed over a recency-weighted copy
// of the envelope (exponential, time constant recencyTauSeconds) so that when the
// input tempo changes, stale evidence from the old tempo decays quickly instead of
// dominating until it ages out of the window. This cuts the high→low transition lag
// without shortening the window (slow tempos still need the full span to lock).
//
// Pure and stateless apart from the last result — no threads, no I/O — so it is
// unit-testable by feeding synthetic OSF arrays (see AutocorrelationTempoEstimatorTests).
public sealed class AutocorrelationTempoEstimator
{
    // HarmonicSupport: how strongly the chosen period repeats — ac[2L]/ac[L] in [0,1].
    // High for a genuinely periodic train (the autocorrelation also peaks at twice the
    // lag), ~0 for a one-off interval such as the transient gap thrown off while the
    // input tempo changes. The TempoSmoother uses it to adopt a corroborated up-jump
    // immediately instead of waiting out the confirmation window. 0 when 2L is outside
    // the analysed lag range (period too slow to test the harmonic).
    public readonly record struct TempoEstimate(int Bpm, float Confidence, float HarmonicSupport);

    // Subharmonic rejection: when promoting the global-max lag down to a divisor
    // (½, ⅓, ¼ tempo → the true period), that divisor lag must itself be a local
    // maximum standing at least this fraction of the global-max strength. A strong
    // accent can pull the fundamental peak well below the sub-tempo peak, so the bar
    // is deliberately low; it stays safe because only the exact divisor lags are
    // tested (not every short lag), and on a clean train those land on troughs.
    private const double SubharmonicPeakFraction = 0.30;

    // Largest integer subdivision considered when undoing a sub-tempo pick: a global
    // peak up to 4× the true period (e.g. a 25 BPM read of a 100 BPM click) is folded
    // back up. Beyond ¼ the divisor lags get too close to the noise floor to trust.
    private const int MaxSubharmonicFactor = 4;

    // Half-width (in lag samples) of the neighbourhood searched around L/factor, to
    // absorb the integer rounding of the division and any sub-sample offset of the peak.
    private const int SubharmonicSearchRadius = 3;

    private readonly double _minBpm;
    private readonly double _maxBpm;
    private readonly double _preferredCenter;
    private readonly double _preferredSigma;
    private readonly double _recencyTauSeconds;

    public AutocorrelationTempoEstimator(
        double minBpm,
        double maxBpm,
        double preferredCenter,
        double preferredSigma,
        double recencyTauSeconds = 0.0)
    {
        _minBpm            = minBpm;
        _maxBpm            = maxBpm;
        _preferredCenter   = preferredCenter;
        _preferredSigma    = preferredSigma;
        _recencyTauSeconds = recencyTauSeconds;
    }

    // Estimate tempo from an OSF snapshot. hopMs is the time between OSF samples.
    // When fold is true the dominant peak is octave-folded toward the preferred
    // tempo range; when false the dominant peak is reported as-is.
    public TempoEstimate Analyze(ReadOnlySpan<double> osf, double hopMs, bool fold = true)
    {
        int n = osf.Length;
        if (n < 8 || hopMs <= 0.0)
            return new TempoEstimate(0, 0f, 0f);

        // Lag bounds (in OSF samples) for the supported BPM range. A faster tempo
        // ⇒ shorter period ⇒ smaller lag. Cap lagMax at n/2 so every lag still has
        // at least half the window of overlapping terms.
        int lagMin = (int)Math.Floor(60_000.0 / (_maxBpm * hopMs));
        int lagMax = (int)Math.Ceiling(60_000.0 / (_minBpm * hopMs));
        lagMin = Math.Max(1, lagMin);
        lagMax = Math.Min(lagMax, n / 2);
        if (lagMax <= lagMin)
            return new TempoEstimate(0, 0f, 0f);

        // Recency weighting: emphasise recent OSF so a tempo change is not masked by
        // stale evidence still in the window. The snapshot is ordered oldest→newest
        // (index 0 oldest, n-1 newest), so weight w[i] = exp(-(n-1-i)/τ) gives the
        // newest sample weight 1 and decays toward the past with time constant τ.
        // We fold √w into the centred signal (cw[i] = √w[i]·(x[i]-mean)) so the
        // existing autocorrelation math below is literally the autocorrelation of the
        // weighted signal — still bounded in ~[-1, 1], same short-lag bias, and the
        // subharmonic/interpolation/fold steps need no change. With τ ≤ 0 the weights
        // collapse to 1, reproducing the original unweighted estimate exactly.
        // The full window length is kept (so slow tempos still have enough span); only
        // the *influence* of old samples decays, which lets a high→low change surface
        // once the new tempo fills ~60 % of the window (~3.5 s at the default τ) instead
        // of ~80 % (~4.8 s) — see documentation/SoundModeImplementation.md §5.3.
        double tauSamples = _recencyTauSeconds > 0.0 ? _recencyTauSeconds * 1000.0 / hopMs : 0.0;

        double[] cw = new double[n];

        // Weighted mean m = Σ w[i]·x[i] / Σ w[i], so the centred signal has weighted
        // zero mean (the correct DC removal for a weighted autocorrelation).
        double sumW = 0.0, sumWX = 0.0;
        for (int i = 0; i < n; i++)
        {
            double w = tauSamples > 0.0 ? Math.Exp(-(n - 1 - i) / tauSamples) : 1.0;
            cw[i] = w;            // stash w; overwritten with √w·(x-mean) in the next pass
            sumW  += w;
            sumWX += w * osf[i];
        }
        double mean = sumWX / sumW;

        // Total weighted squared deviation (= weighted lag-0 autocorrelation) normalises
        // the result into ~[-1, 1]. A flat/silent envelope has ~zero deviation → no tempo.
        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            double sqrtW = Math.Sqrt(cw[i]);
            double d     = sqrtW * (osf[i] - mean);
            cw[i]  = d;
            sumSq += d * d;
        }
        if (sumSq <= 1e-12)
            return new TempoEstimate(0, 0f, 0f);

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
                sum += cw[i] * cw[i + lag];

            double norm = sum / sumSq;
            ac[lag] = norm;

            if (norm > bestAc)
            {
                bestAc  = norm;
                bestLag = lag;
            }
        }

        if (bestAc <= 0.0)
            return new TempoEstimate(0, 0f, 0f);

        // Subharmonic rejection (octave-down correction). The autocorrelation of a
        // periodic onset train peaks at the true period AND every integer multiple of
        // it, so the global-max lag can be a *sub-tempo*: when the envelope has a strong
        // accent pattern (e.g. a metronome whose accented and unaccented clicks differ
        // in kick/bass-band energy, so every other click dominates) the half-tempo peak
        // wins and the readout flaps between a tempo and its divisors (the 100→50→25
        // jump). The true beat then sits at lag L/2, L/3 … as a clear local maximum.
        // Promote to the shortest divisor lag (largest factor ⇒ fastest tempo) that is a
        // local maximum above SubharmonicPeakFraction of the global peak. A clean train's
        // divisor lags fall on troughs, so it is left untouched.
        for (int factor = MaxSubharmonicFactor; factor >= 2; factor--)
        {
            // Integer rounding of L/factor can land a sample beside the true peak, so
            // search a small neighbourhood for the strongest lag rather than testing the
            // rounded lag alone (which would fail the local-max test on the peak's flank).
            int center = (int)Math.Round((double)bestLag / factor);
            if (center <= lagMin || center >= lagMax) continue;

            int lo = Math.Max(lagMin + 1, center - SubharmonicSearchRadius);
            int hi = Math.Min(lagMax - 1, center + SubharmonicSearchRadius);
            int sub = lo;
            for (int lag = lo + 1; lag <= hi; lag++)
                if (ac[lag] > ac[sub]) sub = lag;

            if (ac[sub] < bestAc * SubharmonicPeakFraction) continue;
            if (ac[sub] >= ac[sub - 1] && ac[sub] > ac[sub + 1])
            {
                bestLag = sub;
                bestAc  = ac[sub];
                break;
            }
        }

        // Self-harmonic support of the chosen period (after subharmonic promotion): a
        // genuinely repeating train also correlates at twice the lag (every-other-beat),
        // so the half-tempo peak is a substantial fraction of ac[L]; a one-off interval
        // (the transient gap during a tempo change) has ~no energy there. 0 when 2L lies
        // outside the analysed range (period slower than ~half the longest testable lag).
        // bestAc > 0 here (guarded above), so the division is safe. We search a small
        // neighbourhood around 2L rather than the exact doubled lag: when the fundamental
        // sits between integer lags (e.g. 181 BPM → L=57, true 2L≈113.5), ac[2·L] lands on
        // the peak's flank and badly understates support — the same integer-rounding fix
        // the subharmonic search above uses.
        float harmonicSupport = 0f;
        int twoLag = 2 * bestLag;
        if (twoLag <= lagMax)
        {
            int lo = Math.Max(lagMin, twoLag - SubharmonicSearchRadius);
            int hi = Math.Min(lagMax, twoLag + SubharmonicSearchRadius);
            double peak = ac[lo];
            for (int lag = lo + 1; lag <= hi; lag++)
                if (ac[lag] > peak) peak = ac[lag];
            harmonicSupport = (float)Math.Clamp(Math.Max(0.0, peak) / bestAc, 0.0, 1.0);
        }

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

        return new TempoEstimate((int)Math.Round(bpm), confidence, harmonicSupport);
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
