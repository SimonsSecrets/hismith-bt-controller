namespace HismithController.SoundMode;

// Pure, stateless mapping from the detected music tempo to the device tempo.
// Applies the thrust-rhythm divider (§4.1) then the max-speed cap (§4.2). The
// device-send wiring (Phase 3) stays out of this class entirely so it remains
// trivially unit-testable.
public static class BeatToDeviceMapper
{
    // deviceBpm = round(min(musicBpm / ratio, maxBpm)). The cap operates on the
    // unrounded post-ratio value (design §6.4), then the result is rounded.
    public static int Map(int musicBpm, ThrustRhythm rhythm, int maxBpm)
    {
        double postRatio = musicBpm / (double)(int)rhythm;
        return (int)Math.Round(Math.Min(postRatio, maxBpm));
    }
}
