using HismithController.SoundMode;

namespace HismithController.Configuration;

// User-tunable settings that persist between sessions, stored separately from the
// bundled read-only AppConfig.json (which carries deploy flags + detector tunables).
public sealed class UserPreferences
{
    public ThrustRhythm SelectedRhythm { get; set; } = ThrustRhythm.EveryBeat;

    // Sound Mode device-speed cap; 240 = uncapped (full scale).
    public int MaxBpm { get; set; } = 240;

    // Appearance. Defaults to System (follow the OS) for a first run with no saved file.
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    // First-run flag for the welcome overlay. Defaults to false so a fresh install (no settings
    // file) shows the overlay once; set true when the user dismisses it via "Get started".
    public bool HasSeenWelcome { get; set; }
}
