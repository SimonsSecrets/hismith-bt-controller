using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Configuration;
using Microsoft.Win32;

namespace HismithController.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    // App metadata surfaced on the About card (matches design/settings.jsx constants).
    private const string AuthorName = "SimonsSecrets";
    public const string ContactEmail = "simonssecrets@gmail.com";
    private const string KofiUrl = "https://ko-fi.com/simonssecrets";

    private readonly AppDataPaths _appDataPaths;
    private readonly UserPreferencesStore _prefsStore;

    // The active appearance choice. Two-way bound from the Light/Dark/System segmented
    // control; the setter applies the theme app-wide and persists it.
    [ObservableProperty]
    private ThemePreference _theme;

    // The resolved active data folder, shown in the path field. Updated after a successful
    // change so the field reflects the new location before the restart.
    [ObservableProperty]
    private string _dataFolder;

    public string Author => AuthorName;
    public string Contact => ContactEmail;

    // Informational version (e.g. "0.1.0"), falling back to the assembly version. Set via
    // <Version> in the csproj so it tracks builds.
    public string Version { get; }

    public SettingsViewModel(AppDataPaths appDataPaths, UserPreferencesStore prefsStore)
    {
        _appDataPaths = appDataPaths;
        _prefsStore = prefsStore;

        _dataFolder = appDataPaths.DataFolder;
        _theme = prefsStore.Load().Theme;
        Version = ResolveVersion();
    }

    // Applies the new appearance immediately and persists it (load-modify-save so the Sound
    // Mode fields in the same file are untouched).
    partial void OnThemeChanged(ThemePreference value)
    {
        if (Application.Current is App app)
            app.ApplyThemePreference(value);
        _prefsStore.Update(p => p.Theme = value);
    }

    [RelayCommand]
    private void ChangeDataFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose data folder",
            InitialDirectory = _appDataPaths.DataFolder
        };
        if (dialog.ShowDialog() != true)
            return;

        if (!_appDataPaths.ChangeDataFolder(dialog.FolderName, out var error))
        {
            MessageBox.Show(error ?? "The data folder could not be changed.",
                "Data folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DataFolder = _appDataPaths.DataFolder;

        // The logger and preferences store were bound to the old folder at startup; a restart
        // re-points every subsystem to the new location cleanly (see AppDataPaths).
        var restart = MessageBox.Show(
            "Your data folder has been updated. Restart now to start using the new location?",
            "Restart required", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (restart == MessageBoxResult.Yes)
            RestartApp();
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenShell(_appDataPaths.DataFolder);

    [RelayCommand]
    private void OpenContact() => OpenShell($"mailto:{ContactEmail}");

    [RelayCommand]
    private void OpenKofi() => OpenShell(KofiUrl);

    private static void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true }); }
            catch { /* if relaunch fails, still shut down so the new folder takes effect on next manual launch */ }
        }
        Application.Current.Shutdown();
    }

    // Shell-execute so the OS resolves folders, mailto: and https: with the right handler.
    private static void OpenShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // No handler / blocked — silently ignore; this is a convenience action.
        }
    }

    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var informational = asm
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // Informational version can carry a build suffix (e.g. "0.1.0+abc123"); trim it.
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];
        return asm.GetName().Version?.ToString(3) ?? "0.1.0";
    }
}
