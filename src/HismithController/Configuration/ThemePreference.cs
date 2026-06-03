namespace HismithController.Configuration;

// The user's chosen appearance. System follows the OS light/dark setting (live);
// Light/Dark pin a fixed look. Persisted by name in user-settings.json so reordering
// members can't silently change what a saved file means.
public enum ThemePreference
{
    Light,
    Dark,
    System
}
