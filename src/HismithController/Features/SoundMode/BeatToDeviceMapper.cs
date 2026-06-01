namespace HismithController.SoundMode;

// Pure, stateless mapping from the detected music tempo to the device tempo.
// Phase 4.1 applies only the thrust-rhythm divider; the max-speed cap (§4.2) slots
// in here later as Math.Min(deviceBpm, maxBpm), and the device-send wiring (Phase 3)
// stays out of this class entirely so it remains trivially unit-testable.
public static class BeatToDeviceMapper
{
    // deviceBpm = round(musicBpm / ratio). The ratio is the ThrustRhythm's integer value.
    public static int Map(int musicBpm, ThrustRhythm rhythm)
        => (int)Math.Round(musicBpm / (double)(int)rhythm);
}
