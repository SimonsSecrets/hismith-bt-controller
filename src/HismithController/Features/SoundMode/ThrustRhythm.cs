namespace HismithController.SoundMode;

// Beats-per-stroke ratio for Sound Mode. The enum's integer value IS the divider
// applied to the detected music BPM (deviceBpm = musicBpm / ratio), so it can be
// cast to int and used directly as the ratio — see BeatToDeviceMapper.
public enum ThrustRhythm
{
    EveryBeat = 1,
    EveryTwoBeats = 2,
    EveryFourBeats = 4
}
