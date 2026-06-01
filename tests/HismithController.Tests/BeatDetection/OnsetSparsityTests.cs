using HismithController.BeatDetection;
using Xunit.Abstractions;

namespace HismithController.Tests.BeatDetection;

// Validates the regime classifier's OSF sparsity metric. The key requirement (and
// the source of an earlier metronome regression): a click train must read as
// "sparse" at ANY tempo and even over a steady noise floor, so it is never routed
// to the octave-folding autocorrelation path. Continuous content must read "dense".
public class OnsetSparsityTests
{
    private readonly ITestOutputHelper _out;
    public OnsetSparsityTests(ITestOutputHelper output) => _out = output;

    private const int N = 1024;

    // Default classifier thresholds (mirror AppSettings).
    private const double MetronomeMin = 0.60;
    private const double DenseMax     = 0.40;

    private static double[] ImpulseTrain(int periodHops, double spike, double floorLevel, int seed)
    {
        var rng = new Random(seed);
        var osf = new double[N];
        for (int i = 0; i < N; i++)
            osf[i] = floorLevel * rng.NextDouble(); // fluctuating noise floor
        for (int i = 0; i < N; i += periodHops)
            osf[i] = spike;
        return osf;
    }

    [Theory]
    [InlineData(12, 0.0)]   // 120 BPM, perfect silence between clicks
    [InlineData(12, 0.02)]  // 120 BPM, low noise floor
    [InlineData(12, 0.10)]  // 120 BPM, moderate noise floor
    [InlineData(43, 0.10)]  // 70 BPM,  moderate noise floor
    [InlineData(6,  0.10)]  // 240 BPM, moderate noise floor (fast metronome)
    public void CleanMetronome_IsSparse_RegardlessOfNoiseFloor(int periodHops, double floorLevel)
    {
        var osf = ImpulseTrain(periodHops, spike: 1.0, floorLevel, seed: 1);
        double s = SpectralFluxBeatDetector.ComputeSparsity(osf);
        _out.WriteLine($"metronome period={periodHops} floor={floorLevel} sparsity={s:F3}");
        Assert.True(s >= MetronomeMin, $"expected sparse (≥{MetronomeMin}) but was {s:F3}");
    }

    [Fact]
    public void ContinuousNoise_IsDense()
    {
        var rng = new Random(2);
        var osf = new double[N];
        for (int i = 0; i < N; i++) osf[i] = rng.NextDouble(); // broadband, no peaks
        double s = SpectralFluxBeatDetector.ComputeSparsity(osf);
        _out.WriteLine($"continuous-noise sparsity={s:F3}");
        Assert.True(s <= DenseMax, $"expected dense (≤{DenseMax}) but was {s:F3}");
    }

    [Fact]
    public void DenseMusic_ManyOnsetsPerWindow_IsDense()
    {
        // Onsets on every subdivision plus sustained energy → envelope stays elevated.
        var rng = new Random(3);
        var osf = new double[N];
        for (int i = 0; i < N; i++) osf[i] = 0.3 + 0.4 * rng.NextDouble();
        for (int i = 0; i < N; i += 3) osf[i] += 0.5; // frequent onsets
        double s = SpectralFluxBeatDetector.ComputeSparsity(osf);
        _out.WriteLine($"dense-music sparsity={s:F3}");
        Assert.True(s <= DenseMax, $"expected dense (≤{DenseMax}) but was {s:F3}");
    }
}
