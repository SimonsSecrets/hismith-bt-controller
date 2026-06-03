using System.IO;

namespace HismithController.Configuration;

// Resolves the application's data folder — the single directory that holds the user's
// preferences (user-settings.json) and the rolling log files. The folder is user-editable
// via the Settings screen, so its location cannot itself live inside that folder (it would
// have nowhere to bootstrap from). Instead a tiny fixed pointer file at a well-known
// %LOCALAPPDATA% path records the active folder; absent/blank/unreadable falls back to the
// default. Resolved once at construction (registered as a DI singleton) so every subsystem
// agrees on one folder for the lifetime of the process — a folder change takes effect after
// a restart (see ChangeDataFolder).
public sealed class AppDataPaths
{
    private const string LogsFolderName = "logs";
    private const string UserSettingsFileName = "user-settings.json";

    public AppDataPaths()
    {
        DataFolder = ResolveDataFolder();
    }

    public string DataFolder { get; private set; }

    public string LogsFolder => Path.Combine(DataFolder, LogsFolderName);

    public string UserSettingsPath => Path.Combine(DataFolder, UserSettingsFileName);

    // Fixed bootstrap root, never moves: %LOCALAPPDATA%\HismithController. Also the default
    // data folder, so existing installs (which already wrote logs/settings here) keep working
    // with no pointer file present.
    public static string DefaultDataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HismithController");

    // Single line holding the active data folder. Lives at the fixed root, NOT inside
    // DataFolder, so it survives the folder being repointed.
    private static string PointerFilePath => Path.Combine(DefaultDataFolder, "data-location.txt");

    private static string ResolveDataFolder()
    {
        try
        {
            if (File.Exists(PointerFilePath))
            {
                var stored = File.ReadAllText(PointerFilePath).Trim();
                // A custom folder is only honoured if it still exists; a deleted/renamed
                // target falls back to the default rather than failing every write.
                if (!string.IsNullOrWhiteSpace(stored) && Directory.Exists(stored))
                    return stored;
            }
        }
        catch
        {
            // Unreadable pointer → default. Path resolution must never throw into startup.
        }
        return DefaultDataFolder;
    }

    // Validates the target, migrates existing data into it, and persists it as the new active
    // folder. Does NOT re-point the already-running logger/preferences store — the caller is
    // expected to prompt for a restart so all subsystems pick up the new location cleanly.
    // Returns false (with a user-facing message in error) on validation/IO failure.
    public bool ChangeDataFolder(string newFolder, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(newFolder))
        {
            error = "No folder selected.";
            return false;
        }

        newFolder = Path.GetFullPath(newFolder);

        if (string.Equals(newFolder, DataFolder, StringComparison.OrdinalIgnoreCase))
            return true;   // no-op: already the active folder

        try
        {
            Directory.CreateDirectory(newFolder);

            // Fail early with a clear message if the folder isn't writable, rather than
            // silently losing later log/settings writes.
            var probe = Path.Combine(newFolder, ".write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            error = $"That folder can't be used: {ex.Message}";
            return false;
        }

        MigrateData(DataFolder, newFolder);

        try
        {
            Directory.CreateDirectory(DefaultDataFolder);   // the pointer's own fixed root
            File.WriteAllText(PointerFilePath, newFolder);
        }
        catch (Exception ex)
        {
            error = $"Couldn't save the new location: {ex.Message}";
            return false;
        }

        DataFolder = newFolder;
        return true;
    }

    // Best-effort copy of the preferences file and the logs folder into the new location.
    // Files that are locked (e.g. today's open log writer) are skipped — the logger will
    // simply create a fresh file in the new folder after the restart. A failed migration is
    // never fatal: at worst the new folder starts empty.
    private static void MigrateData(string from, string to)
    {
        try
        {
            var srcSettings = Path.Combine(from, UserSettingsFileName);
            if (File.Exists(srcSettings))
                CopyFileSafe(srcSettings, Path.Combine(to, UserSettingsFileName));

            var srcLogs = Path.Combine(from, LogsFolderName);
            if (Directory.Exists(srcLogs))
            {
                var dstLogs = Path.Combine(to, LogsFolderName);
                Directory.CreateDirectory(dstLogs);
                foreach (var file in Directory.EnumerateFiles(srcLogs))
                    CopyFileSafe(file, Path.Combine(dstLogs, Path.GetFileName(file)));
            }
        }
        catch
        {
            // Migration is opportunistic; never block the folder change on it.
        }
    }

    private static void CopyFileSafe(string source, string destination)
    {
        try
        {
            File.Copy(source, destination, overwrite: true);
        }
        catch
        {
            // Locked/open file (e.g. current day's log) — skip it.
        }
    }
}
