using HismithController.BeatDetection;

namespace HismithController.Tests.BeatDetection;

// AutocorrelationTempoEstimator is a pure function over an onset-strength envelope
// (OSF). Tests synthesise OSF impulse trains for known tempos and assert on the
// estimated BPM.
public class AutocorrelationTempoEstimatorTests
{
    // Matches SpectralFluxBeatDetector: 256-sample hop at 44100 Hz ≈ 5.805 ms.
    private const double HopMs = 256.0 * 1000.0 / 44100.0;

    // 6 s of OSF, matching the default window.
    private const int OsfLen = (int)(6.0 * 44100 / 256);

    private static AutocorrelationTempoEstimator Default() =>
        new(minBpm: 15.0, maxBpm: 240.0, preferredCenter: 120.0, preferredSigma: 0.5);

    // Build an OSF with an onset bump every `beatBpm` beat, optionally adding equal
    // off-beat bumps at twice that rate (subdivisionsPerBeat = 2 ⇒ eighth notes).
    // Bumps span a few hops (triangular kernel) like a real spectral-flux onset, so
    // autocorrelation is not defeated by single-sample rounding jitter.
    private static double[] ImpulseTrain(double beatBpm, int subdivisionsPerBeat = 1)
    {
        var osf = new double[OsfLen];
        double pulseMs = 60_000.0 / (beatBpm * subdivisionsPerBeat);
        double pulseSamples = pulseMs / HopMs;
        ReadOnlySpan<double> kernel = stackalloc double[] { 0.5, 1.0, 0.5 };
        for (int k = 0; ; k++)
        {
            int center = (int)Math.Round(k * pulseSamples);
            if (center - 1 >= OsfLen) break;
            for (int j = 0; j < kernel.Length; j++)
            {
                int idx = center - 1 + j;
                if (idx >= 0 && idx < OsfLen) osf[idx] = Math.Max(osf[idx], kernel[j]);
            }
        }
        return osf;
    }

    [Fact]
    public void Analyze_CleanTrain120_ReturnsAbout120()
    {
        var est = Default();
        var result = est.Analyze(ImpulseTrain(120), HopMs);
        Assert.InRange(result.Bpm, 117, 123);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(150)]
    public void Analyze_CleanTrain_ReturnsApproxTempo(int bpm)
    {
        var est = Default();
        var result = est.Analyze(ImpulseTrain(bpm), HopMs);
        Assert.InRange(result.Bpm, bpm - 3, bpm + 3);
    }

    [Fact]
    public void Analyze_EighthNoteHeavyTrain_FoldsToQuarterTempo()
    {
        // Equal-magnitude onsets every eighth note of a 120 BPM tune. The raw
        // dominant period is the eighth (≈240 BPM); octave folding must report 120.
        var est = Default();
        var result = est.Analyze(ImpulseTrain(120, subdivisionsPerBeat: 2), HopMs);
        Assert.InRange(result.Bpm, 117, 123);
    }

    [Fact]
    public void Analyze_FastTrainWithoutFolding_StaysFast()
    {
        // With folding disabled the dominant period is reported as-is, preserving
        // full 15–240 range (the path the metronome/sparse regime relies on).
        var est = Default();
        var result = est.Analyze(ImpulseTrain(200), HopMs, fold: false);
        Assert.InRange(result.Bpm, 196, 204);
    }

    // Build a 100 BPM train with an accented downbeat every `accentEvery` beats:
    // the accented onset is full magnitude, the others are quieter. The accent injects
    // a strong subharmonic period (100/accentEvery BPM) into the autocorrelation, the
    // pattern that made the old global-max picker flap between 100 and its divisors.
    private static double[] AccentedTrain(double beatBpm, int accentEvery, double weakMag)
    {
        var osf = new double[OsfLen];
        double pulseSamples = (60_000.0 / beatBpm) / HopMs;
        ReadOnlySpan<double> kernel = stackalloc double[] { 0.5, 1.0, 0.5 };
        for (int k = 0; ; k++)
        {
            int center = (int)Math.Round(k * pulseSamples);
            if (center - 1 >= OsfLen) break;
            double mag = (k % accentEvery == 0) ? 1.0 : weakMag;
            for (int j = 0; j < kernel.Length; j++)
            {
                int idx = center - 1 + j;
                if (idx >= 0 && idx < OsfLen) osf[idx] = Math.Max(osf[idx], mag * kernel[j]);
            }
        }
        return osf;
    }

    [Theory]
    [InlineData(2, 0.3)]   // accent every 2 beats → strong 50 BPM subharmonic
    [InlineData(4, 0.15)]  // accent every 4 beats → strong 25 BPM subharmonic
    public void Analyze_AccentedMetronome_ReportsBeatNotSubharmonic(int accentEvery, double weakMag)
    {
        // A metronome whose accented/unaccented clicks differ in kick/bass-band energy
        // makes the half/quarter-tempo autocorrelation peak win, so a plain global-max
        // pick flapped to 50/25. Subharmonic rejection must lock to the click rate (100),
        // the regime the unfolded metronome path relies on.
        var est = Default();
        var result = est.Analyze(AccentedTrain(100, accentEvery, weakMag), HopMs, fold: false);
        Assert.InRange(result.Bpm, 97, 103);
    }

    [Fact]
    public void Analyze_EmptyEnvelope_ReturnsZero()
    {
        var est = Default();
        var result = est.Analyze(Array.Empty<double>(), HopMs);
        Assert.Equal(0, result.Bpm);
        Assert.Equal(0f, result.Confidence);
    }

    [Fact]
    public void Analyze_FlatEnvelope_ReturnsZero()
    {
        // A constant (zero-variance) envelope has no periodicity.
        var est = Default();
        var flat = new double[OsfLen];
        Array.Fill(flat, 0.5);
        var result = est.Analyze(flat, HopMs);
        Assert.Equal(0, result.Bpm);
        Assert.Equal(0f, result.Confidence);
    }

    [Fact]
    public void Analyze_CleanTrain_HasPositiveConfidence()
    {
        var est = Default();
        var result = est.Analyze(ImpulseTrain(128), HopMs);
        Assert.True(result.Confidence > 0f);
    }
}
