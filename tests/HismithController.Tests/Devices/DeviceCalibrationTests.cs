using HismithController.Devices;

namespace HismithController.Tests.Devices;

// DeviceCalibration maps a target tempo (BPM) to the speed percent that actually
// produces it on a given model, and back. The Pro 1 curve is measured (OpenPoints §3)
// and non-linear; Linear() is the fallback for unmeasured models.
public class DeviceCalibrationTests
{
    // The empirically-measured Pro 1 (AK Series) response, mirroring the catalog.
    private static DeviceCalibration Pro1() => new(
    [
        (0, 0), (10, 38), (20, 62), (30, 85), (40, 112), (50, 136),
        (60, 160), (70, 186), (80, 213), (90, 234), (100, 240),
    ]);

    [Fact]
    public void MaxBpm_IsTopCalibrationPoint()
        => Assert.Equal(240, Pro1().MaxBpm);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 136)]
    [InlineData(100, 240)]
    public void PercentToBpm_ReturnsMeasuredPoints(int percent, int expectedBpm)
        => Assert.Equal(expectedBpm, Pro1().PercentToBpm(percent));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(136, 50)]
    [InlineData(240, 100)]
    public void BpmToPercent_ReturnsMeasuredPoints(int bpm, int expectedPercent)
        => Assert.Equal(expectedPercent, Pro1().BpmToPercent(bpm));

    // The whole point of §3: a linear scale maps 120 BPM → 50 %, but 50 % actually
    // produces 136 BPM. The calibration emits the lower percent that yields 120.
    [Fact]
    public void BpmToPercent_NonLinear_CorrectsLinearOvershoot()
    {
        // 120 sits in the 40 % (112) → 50 % (136) segment: 40 + (120-112)/(136-112)*10 ≈ 43.
        Assert.Equal(43, Pro1().BpmToPercent(120));
    }

    [Fact]
    public void PercentToBpm_InterpolatesBetweenPoints()
    {
        // 45 % sits halfway between 40 % (112) and 50 % (136) → 124.
        Assert.Equal(124, Pro1().PercentToBpm(45));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(300, 100)]
    public void BpmToPercent_ClampsOutOfRange(int bpm, int expectedPercent)
        => Assert.Equal(expectedPercent, Pro1().BpmToPercent(bpm));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(150, 240)]
    public void PercentToBpm_ClampsOutOfRange(int percent, int expectedBpm)
        => Assert.Equal(expectedBpm, Pro1().PercentToBpm(percent));

    // Round-trip is only approximate (each direction rounds to an int), but staying
    // within a couple BPM confirms the forward and inverse curves are consistent.
    [Theory]
    [InlineData(38)]
    [InlineData(112)]
    [InlineData(200)]
    public void RoundTrip_BpmToPercentToBpm_IsClose(int bpm)
    {
        var cal = Pro1();
        var back = cal.PercentToBpm(cal.BpmToPercent(bpm));
        Assert.True(Math.Abs(back - bpm) <= 3, $"round-trip {bpm} → {back}");
    }

    // Linear() reproduces the old bpm*100/maxBpm mapping exactly for unmeasured models.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    public void Linear_MatchesProportionalMapping(int bpm, int expectedPercent)
        => Assert.Equal(expectedPercent, DeviceCalibration.Linear(100).BpmToPercent(bpm));

    [Fact]
    public void Constructor_RejectsNonMonotonicPoints()
        => Assert.Throws<ArgumentException>(() =>
            new DeviceCalibration([(0, 0), (50, 100), (40, 120)]));

    [Fact]
    public void Constructor_RejectsDecreasingBpm()
        => Assert.Throws<ArgumentException>(() =>
            new DeviceCalibration([(0, 0), (50, 120), (100, 100)]));

    [Fact]
    public void Constructor_RejectsTooFewPoints()
        => Assert.Throws<ArgumentException>(() => new DeviceCalibration([(0, 0)]));
}
