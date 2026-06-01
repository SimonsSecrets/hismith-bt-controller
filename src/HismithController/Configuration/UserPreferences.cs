using HismithController.SoundMode;

namespace HismithController.Configuration;

// User-tunable settings that persist between sessions, stored separately from the
// bundled read-only AppConfig.json (which carries deploy flags + detector tunables).
public sealed class UserPreferences
{
    public ThrustRhythm SelectedRhythm { get; set; } = ThrustRhythm.EveryBeat;

    // Sound Mode device-speed cap; 240 = uncapped (full scale).
    public int MaxBpm { get; set; } = 240;
}
