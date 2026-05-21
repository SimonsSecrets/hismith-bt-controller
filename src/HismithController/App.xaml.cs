using System.Windows;
using HismithController.Audio;
using HismithController.BeatDetection;
using HismithController.Bluetooth;
using HismithController.Configuration;
using HismithController.Devices;
using HismithController.Logging;
using HismithController.Services;
using HismithController.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HismithController;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

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
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("AppConfig.json", optional: true, reloadOnChange: false)
            .Build();

        var settings = new AppSettings();
        configuration.Bind(settings);

        // --mock implies both axes; --mock-ble / --mock-audio control each axis independently.
        bool mockBle = settings.UseMockBle
            || e.Args.Contains("--mock", StringComparer.OrdinalIgnoreCase)
            || e.Args.Contains("--mock-ble", StringComparer.OrdinalIgnoreCase);
        bool mockAudio = settings.UseMockAudio
            || e.Args.Contains("--mock", StringComparer.OrdinalIgnoreCase)
            || e.Args.Contains("--mock-audio", StringComparer.OrdinalIgnoreCase);

        settings.UseMockBle = mockBle;
        settings.UseMockAudio = mockAudio;

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(new FileLoggerProvider(FileLoggerProvider.DefaultLogDirectory));
        });
        services.AddSingleton(settings);

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
        services.AddSingleton<SpectrumAnalyzer>();
        services.AddSingleton<IBeatDetector, SpectralFluxBeatDetector>();
        services.AddSingleton<IConnectedDeviceService, ConnectedDeviceService>();
        services.AddSingleton<ConnectionViewModel>();
        services.AddSingleton<ManualModeViewModel>();
        services.AddSingleton<SoundModeViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        DetectSystemTheme();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
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
    }

    private void DetectSystemTheme()
    {
        try
        {
            var uiSettings = new global::Windows.UI.ViewManagement.UISettings();
            var bg = uiSettings.GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Background);
            bool systemIsDark = bg.R < 128;

            if (systemIsDark)
            {
                ApplyTheme(true);
                var vm = _serviceProvider?.GetService<MainViewModel>();
                if (vm is not null)
                    vm.IsDarkTheme = true;
            }
        }
        catch
        {
            // Fallback to light theme
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();

        base.OnExit(e);
    }
}
