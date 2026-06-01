using HismithController.SoundMode;

namespace HismithController.Tests.SoundMode;

// BeatToDeviceMapper is the pure music-BPM → device-BPM mapping. Phase 4.1 applies
// only the thrust-rhythm divider (no cap yet), so these assert the ratio + rounding.
public class BeatToDeviceMapperTests
{
    [Fact]
    public void EveryBeat_PassesThrough()
        => Assert.Equal(100, BeatToDeviceMapper.Map(100, ThrustRhythm.EveryBeat));

    [Fact]
    public void EveryTwoBeats_Halves()
        => Assert.Equal(64, BeatToDeviceMapper.Map(128, ThrustRhythm.EveryTwoBeats));

    [Fact]
    public void EveryFourBeats_Quarters()
        => Assert.Equal(30, BeatToDeviceMapper.Map(120, ThrustRhythm.EveryFourBeats));

    [Fact]
    public void ZeroMusicBpm_MapsToZero()
        => Assert.Equal(0, BeatToDeviceMapper.Map(0, ThrustRhythm.EveryFourBeats));

    // Math.Round defaults to banker's rounding (MidpointRounding.ToEven): 130/4 = 32.5 → 32.
    [Fact]
    public void Midpoint_RoundsToEven()
        => Assert.Equal(32, BeatToDeviceMapper.Map(130, ThrustRhythm.EveryFourBeats));
}
