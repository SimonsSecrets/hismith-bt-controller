using System.Windows;
using HismithController.Bluetooth;
using HismithController.Configuration;
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

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("AppConfig.json", optional: true, reloadOnChange: false)
            .Build();

        var settings = new AppSettings();
        configuration.Bind(settings);

        bool useMock = settings.UseMockBle
            || e.Args.Contains("--mock", StringComparer.OrdinalIgnoreCase);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddSingleton(settings);

        if (useMock)
            services.AddSingleton<IBleDeviceService, MockBleDeviceService>();
        else
            services.AddSingleton<IBleDeviceService, HismithBleDeviceService>();

        services.AddSingleton<MainWindow>();
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();

        base.OnExit(e);
    }
}

