using System.Windows;
using HismithController.Bluetooth;

namespace HismithController;

public partial class MainWindow : Window
{
    private readonly IBleDeviceService _bleService;

    public MainWindow(IBleDeviceService bleService)
    {
        _bleService = bleService;
        InitializeComponent();

        _bleService.StatusChanged += (_, status) =>
        {
            Dispatcher.InvokeAsync(() =>
            {
                Title = $"HismithController — {status.State}";
            });
        };
    }
}