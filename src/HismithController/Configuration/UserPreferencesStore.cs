using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HismithController.Configuration;

// Loads/saves UserPreferences as JSON in the per-user app-data folder
// (%LOCALAPPDATA%\HismithController\), the same root the file logger uses. Kept out
// of AppConfig.json because that file lives in the build output (overwritten on every
// rebuild, read-only once installed). All I/O is best-effort: a missing or corrupt
// file falls back to defaults, and a failed write never throws into the app.
public sealed class UserPreferencesStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Persist enums by name so reordering ThrustRhythm members can't silently
        // change what a saved file means.
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public UserPreferencesStore() : this(DefaultPath) { }

    // Test seam: lets unit tests point at a temp file.
    public UserPreferencesStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HismithController",
        "user-settings.json");

    public UserPreferences Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var prefs = JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path), Options);
                if (prefs is not null)
                {
                    // Guard against a hand-edited / corrupt cap value.
                    prefs.MaxBpm = Math.Clamp(prefs.MaxBpm, 0, 240);
                    return prefs;
                }
            }
        }
        catch
        {
            // Unreadable or malformed → defaults.
        }
        return new UserPreferences();
    }

    public void Save(UserPreferences prefs)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(prefs, Options));
        }
        catch
        {
            // Best-effort; a settings write must never crash the app.
        }
    }
}
