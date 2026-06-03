using System.IO;
using HismithController.Configuration;
using HismithController.SoundMode;

namespace HismithController.Tests.SoundMode;

// UserPreferencesStore is the JSON load/save for the persisted Sound Mode settings.
// Tests use a throwaway temp file via the path-injecting constructor.
public class UserPreferencesStoreTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"hismith-prefs-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var prefs = new UserPreferencesStore(_path).Load();

        Assert.Equal(ThrustRhythm.EveryBeat, prefs.SelectedRhythm);
        Assert.Equal(240, prefs.MaxBpm);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new UserPreferencesStore(_path);
        store.Save(new UserPreferences { SelectedRhythm = ThrustRhythm.EveryFourBeats, MaxBpm = 90 });

        var loaded = store.Load();

        Assert.Equal(ThrustRhythm.EveryFourBeats, loaded.SelectedRhythm);
        Assert.Equal(90, loaded.MaxBpm);
    }

    [Fact]
    public void Save_PersistsEnumByName()
    {
        new UserPreferencesStore(_path)
            .Save(new UserPreferences { SelectedRhythm = ThrustRhythm.EveryTwoBeats, MaxBpm = 120 });

        // Name, not numeric value, so reordering the enum can't reinterpret old files.
        Assert.Contains("EveryTwoBeats", File.ReadAllText(_path));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(_path, "{ not valid json ");

        var prefs = new UserPreferencesStore(_path).Load();

        Assert.Equal(ThrustRhythm.EveryBeat, prefs.SelectedRhythm);
        Assert.Equal(240, prefs.MaxBpm);
    }

    [Fact]
    public void Load_ClampsOutOfRangeCap()
    {
        File.WriteAllText(_path, "{\"SelectedRhythm\":\"EveryBeat\",\"MaxBpm\":9999}");

        Assert.Equal(240, new UserPreferencesStore(_path).Load().MaxBpm);
    }

    [Fact]
    public void ThemePreference_RoundTripsByName()
    {
        var store = new UserPreferencesStore(_path);
        store.Save(new UserPreferences { Theme = ThemePreference.Dark });

        Assert.Equal(ThemePreference.Dark, store.Load().Theme);
        // Name, not numeric value — same rationale as the rhythm enum.
        Assert.Contains("Dark", File.ReadAllText(_path));
    }

    [Fact]
    public void HasSeenWelcome_DefaultsToFalseOnMissingFile()
    {
        // A fresh install (no file) must show the welcome overlay once.
        Assert.False(new UserPreferencesStore(_path).Load().HasSeenWelcome);
    }

    [Fact]
    public void HasSeenWelcome_RoundTrips()
    {
        var store = new UserPreferencesStore(_path);
        store.Update(p => p.HasSeenWelcome = true);

        Assert.True(store.Load().HasSeenWelcome);
    }

    [Fact]
    public void Update_PreservesFieldsOwnedByOtherWriters()
    {
        var store = new UserPreferencesStore(_path);
        // Sound Mode writes its two fields...
        store.Update(p => { p.SelectedRhythm = ThrustRhythm.EveryTwoBeats; p.MaxBpm = 120; });
        // ...then the Settings screen writes only the theme.
        store.Update(p => p.Theme = ThemePreference.Light);

        var loaded = store.Load();
        Assert.Equal(ThrustRhythm.EveryTwoBeats, loaded.SelectedRhythm);
        Assert.Equal(120, loaded.MaxBpm);
        Assert.Equal(ThemePreference.Light, loaded.Theme);
    }
}
