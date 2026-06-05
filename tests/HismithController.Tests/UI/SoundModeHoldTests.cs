using HismithController.ViewModels;

namespace HismithController.Tests.UI;

// HoldMsForBpm scales the HasAudio dropout window to the detected tempo so a fast
// source releases the device within a few missed beats instead of a fixed 4 s, while
// slow sources keep the long bridge. The window is DropoutBeats (3) beat periods
// clamped to [1000, 4000] ms. See SoundModeViewModel._audioHoldTimer.
public class SoundModeHoldTests
{
    [Theory]
    [InlineData(120, 1500)] // 3 × 500 ms
    [InlineData(90, 2000)]  // 3 × 667 ms (rounded)
    [InlineData(60, 3000)]  // 3 × 1000 ms
    public void ScalesToThreeBeatPeriods_WithinClamp(int bpm, int expectedMs)
        => Assert.Equal(expectedMs, SoundModeViewModel.HoldMsForBpm(bpm));

    [Theory]
    [InlineData(15)]  // 3 × 4000 ms = 12000, capped
    [InlineData(40)]  // 3 × 1500 ms = 4500, capped
    public void SlowTempo_CappedAtCeiling(int bpm)
        => Assert.Equal(4000, SoundModeViewModel.HoldMsForBpm(bpm));

    [Theory]
    [InlineData(180)] // 3 × 333 ms = 1000, at floor
    [InlineData(240)] // 3 × 250 ms = 750, floored to 1000 (must outlast the 500 ms silence-RMS window)
    public void FastTempo_FlooredSoSilenceIsDetectable(int bpm)
        => Assert.Equal(1000, SoundModeViewModel.HoldMsForBpm(bpm));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NoLock_UsesFullCeiling(int bpm)
        => Assert.Equal(4000, SoundModeViewModel.HoldMsForBpm(bpm));
}
