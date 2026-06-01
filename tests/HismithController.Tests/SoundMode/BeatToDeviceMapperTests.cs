using HismithController.SoundMode;

namespace HismithController.Tests.SoundMode;

// BeatToDeviceMapper is the pure music-BPM → device-BPM mapping: divide by the
// thrust-rhythm ratio (§4.1), then cap at maxBpm (§4.2). 240 = uncapped.
public class BeatToDeviceMapperTests
{
    private const int Uncapped = 240;

    [Fact]
    public void EveryBeat_PassesThrough()
        => Assert.Equal(100, BeatToDeviceMapper.Map(100, ThrustRhythm.EveryBeat, Uncapped));

    [Fact]
    public void EveryTwoBeats_Halves()
        => Assert.Equal(64, BeatToDeviceMapper.Map(128, ThrustRhythm.EveryTwoBeats, Uncapped));

    [Fact]
    public void EveryFourBeats_Quarters()
        => Assert.Equal(30, BeatToDeviceMapper.Map(120, ThrustRhythm.EveryFourBeats, Uncapped));

    [Fact]
    public void ZeroMusicBpm_MapsToZero()
        => Assert.Equal(0, BeatToDeviceMapper.Map(0, ThrustRhythm.EveryFourBeats, Uncapped));

    // Math.Round defaults to banker's rounding (MidpointRounding.ToEven): 130/4 = 32.5 → 32.
    [Fact]
    public void Midpoint_RoundsToEven()
        => Assert.Equal(32, BeatToDeviceMapper.Map(130, ThrustRhythm.EveryFourBeats, Uncapped));

    [Fact]
    public void Cap_LimitsPostRatioValue()
        => Assert.Equal(60, BeatToDeviceMapper.Map(160, ThrustRhythm.EveryTwoBeats, maxBpm: 60));

    [Fact]
    public void Cap_DoesNotRaiseBelowCapValues()
        => Assert.Equal(50, BeatToDeviceMapper.Map(100, ThrustRhythm.EveryTwoBeats, maxBpm: 60));

    // The cap applies to the post-ratio value: 100 BPM uncapped, but ÷2 = 50 ≤ 60 cap.
    [Fact]
    public void Cap_AppliesAfterRatio()
        => Assert.Equal(50, BeatToDeviceMapper.Map(100, ThrustRhythm.EveryTwoBeats, maxBpm: 80));
}
