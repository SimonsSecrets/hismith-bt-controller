using System.Windows;
using HismithController.Audio;
using HismithController.BeatDetection;
using HismithController.Bluetooth;
using HismithController.Configuration;
using HismithController.Devices;
using HismithController.Logging;
using HismithController.Services;
using HismithController.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HismithController;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    // Kept alive for the process lifetime so its ColorValuesChanged event keeps firing
    // (a local would be collected). Used both to detect the OS theme and to follow it live
    // while the user's preference is System.
    private global::Windows.UI.ViewManagement.UISettings? _uiSettings;

    // The active appearance choice. Drives whether ColorValuesChanged re-applies the theme.
    private ThemePreference _themePreference = ThemePreference.System;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandled", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                WriteCrashLog("AppDomain", ex);
        };

        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            WriteCrashLog("Startup", ex);
        }
    }

    private static void WriteCrashLog(string source, Exception ex)
    {
        try
        {
            var dir = FileLoggerProvider.DefaultLogDirectory;
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "crash.txt");
            System.IO.File.WriteAllText(path,
                $"[{DateTimeOffset.Now:yyyy-MM-ddTHH:mm:ss.fffzzz}] [{source}]{Environment.NewLine}{ex}");
        }
        catch
        {
            // Crash logging must never throw.
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        // Mock mode is resolved from startup arguments only — there is no config file.
        // --mock implies both axes; --mock-ble / --mock-audio control each axis independently.
        bool mockBle = e.Args.Contains("--mock", StringComparer.OrdinalIgnoreCase)
            || e.Args.Contains("--mock-ble", StringComparer.OrdinalIgnoreCase);
        bool mockAudio = e.Args.Contains("--mock", StringComparer.OrdinalIgnoreCase)
            || e.Args.Contains("--mock-audio", StringComparer.OrdinalIgnoreCase);

        var settings = new AppSettings
        {
            UseMockBle = mockBle,
            UseMockAudio = mockAudio,
        };

        // Resolve the user-editable data folder up front so the logger and preferences store
        // share one location for the whole process (a folder change applies after a restart).
        var appDataPaths = new AppDataPaths();

        // --capture-osf [or --capture-osf=<path>]: diagnostic recording of the sound-mode tempo
        // pipeline (OpenPoints.md item 2). Bare flag ⇒ a timestamped file under the captures
        // folder; an explicit path after '=' overrides it.
        settings.OsfCapturePath = ResolveOsfCapturePath(e.Args, appDataPaths);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(new FileLoggerProvider(appDataPaths.LogsFolder));
        });
        services.AddSingleton(settings);
        services.AddSingleton(appDataPaths);
        services.AddSingleton(sp =>
            new UserPreferencesStore(sp.GetRequiredService<AppDataPaths>().UserSettingsPath));

        if (mockBle)
        {
            services.AddSingleton<IBleDeviceService, MockBleDeviceService>();
            services.AddSingleton<IDeviceDiscoveryService, MockDeviceDiscoveryService>();
        }
        else
        {
            services.AddSingleton<IBleDeviceService, HismithBleDeviceService>();
            services.AddSingleton<IDeviceDiscoveryService, BleDeviceDiscoveryService>();
        }

        if (mockAudio)
            services.AddSingleton<IAudioCaptureService, MockAudioCaptureService>();
        else
            services.AddSingleton<IAudioCaptureService, WasapiLoopbackAudioCaptureService>();

        // Registered before the detector so DI can inject it. Disposed with the provider on exit
        // (OsfFileCaptureSink flushes/closes its file then), so a final partial cycle isn't lost.
        if (settings.OsfCapturePath is { } capturePath)
            services.AddSingleton<IOsfCaptureSink>(new OsfFileCaptureSink(capturePath));
        else
            services.AddSingleton<IOsfCaptureSink, NullOsfCaptureSink>();

        services.AddSingleton<SpectrumAnalyzer>();
        services.AddSingleton<IBeatDetector, SpectralFluxBeatDetector>();
        services.AddSingleton<IConnectedDeviceService, ConnectedDeviceService>();
        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<ManualModeViewModel>();
        services.AddSingleton<SoundModeViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        // Apply the persisted appearance choice (defaults to System on first run).
        var prefs = _serviceProvider.GetRequiredService<UserPreferencesStore>().Load();
        ApplyThemePreference(prefs.Theme);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    // Returns the OSF capture file path if --capture-osf was passed, else null. Accepts a bare
    // flag (timestamped default file) or --capture-osf=<path> for an explicit location.
    private static string? ResolveOsfCapturePath(string[] args, AppDataPaths paths)
    {
        const string flag = "--capture-osf";
        foreach (var arg in args)
        {
            if (arg.Equals(flag, StringComparison.OrdinalIgnoreCase))
            {
                var name = $"osf-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt";
                return System.IO.Path.Combine(paths.CapturesFolder, name);
            }

            if (arg.StartsWith(flag + "=", StringComparison.OrdinalIgnoreCase))
            {
                var value = arg[(flag.Length + 1)..].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(value))
                    return System.IO.Path.GetFullPath(value);
            }
        }

        return null;
    }

    public void ApplyTheme(bool isDark)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri(
                isDark ? "UI/Themes/DarkTheme.xaml" : "UI/Themes/LightTheme.xaml",
                UriKind.Relative)
        };
        Resources.MergedDictionaries[0] = dict;

        var vm = _serviceProvider?.GetService<MainViewModel>();
        if (vm is not null)
            vm.IsDarkTheme = isDark;
    }

    // Applies a Light/Dark/System choice and arms live OS-theme following for System.
    // Called at startup and whenever the user changes the segmented control in Settings.
    public void ApplyThemePreference(ThemePreference preference)
    {
        _themePreference = preference;

        bool isDark = preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => IsSystemDark()   // System
        };
        ApplyTheme(isDark);

        EnsureSystemThemeWatcher();
    }

    // Subscribes once to the OS color-scheme change so a System preference tracks the OS
    // live. The handler no-ops unless the current preference is still System.
    private void EnsureSystemThemeWatcher()
    {
        if (_uiSettings is not null)
            return;

        try
        {
            _uiSettings = new global::Windows.UI.ViewManagement.UISettings();
            _uiSettings.ColorValuesChanged += (_, _) =>
            {
                if (_themePreference != ThemePreference.System)
                    return;
                // ColorValuesChanged fires on a background thread; touch WPF resources on the UI thread.
                Dispatcher.InvokeAsync(() => ApplyTheme(IsSystemDark()));
            };
        }
        catch
        {
            // UISettings unavailable (rare) → System resolves to light and won't live-update.
        }
    }

    private bool IsSystemDark()
    {
        try
        {
            _uiSettings ??= new global::Windows.UI.ViewManagement.UISettings();
            var bg = _uiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background);
            // A dark background colour ⇒ the OS is in dark mode.
            return bg.R < 128;
        }
        catch
        {
            return false;   // default to light if the OS query fails
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            // Flush any debounced Sound Mode preference write before teardown so a
            // change made within the debounce window right before quitting isn't lost.
            _serviceProvider.GetService<SoundModeViewModel>()?.FlushPendingPreferences();
            await _serviceProvider.DisposeAsync();
        }

        base.OnExit(e);
    }
}
