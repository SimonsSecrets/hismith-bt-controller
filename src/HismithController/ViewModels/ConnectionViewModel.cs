using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Bluetooth;
using HismithController.Services;

namespace HismithController.ViewModels;

public enum ConnectionPhase
{
    PreConnect,
    Scanning,
    DevicesFound,
    NoDevicesFound,
    Connecting,
    ConnectionFailed,
    IncompatibleDevice
}

public partial class ConnectionViewModel : ObservableObject
{
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly IBleDeviceService _bleService;

    [ObservableProperty]
    private ConnectionPhase _phase = ConnectionPhase.PreConnect;

    [ObservableProperty]
    private ObservableCollection<DiscoveredDevice> _discoveredDevices = [];

    [ObservableProperty]
    private DiscoveredDevice? _selectedDevice;

    [ObservableProperty]
    private int _connectStep;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _errorDeviceName;

    public event EventHandler<string>? Connected;

    public ConnectionViewModel(
        IDeviceDiscoveryService discoveryService,
        IBleDeviceService bleService)
    {
        _discoveryService = discoveryService;
        _bleService = bleService;

        _discoveryService.DeviceFound += OnDeviceFound;
        _discoveryService.ScanCompleted += OnScanCompleted;
    }

    [RelayCommand]
    private async Task ScanForDevicesAsync()
    {
        Phase = ConnectionPhase.Scanning;
        DiscoveredDevices.Clear();
        SelectedDevice = null;

        try
        {
            await _discoveryService.StartScanAsync();
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled — state already handled by CancelScan
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _discoveryService.StopScan();
        Phase = DiscoveredDevices.Count > 0
            ? ConnectionPhase.DevicesFound
            : ConnectionPhase.PreConnect;
    }

    [RelayCommand]
    private void SelectDevice(DiscoveredDevice device)
    {
        SelectedDevice = SelectedDevice == device ? null : device;
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync()
    {
        if (SelectedDevice is null)
            return;

        _discoveryService.StopScan();
        var deviceName = SelectedDevice.Name;
        ErrorDeviceName = deviceName;

        Phase = ConnectionPhase.Connecting;
        ConnectStep = 1;

        await Task.Delay(1100);
        ConnectStep = 2;

        await Task.Delay(1300);

        if (!deviceName.Equals("HISMITH", StringComparison.OrdinalIgnoreCase))
        {
            Phase = ConnectionPhase.IncompatibleDevice;
            return;
        }

        try
        {
            await _bleService.ConnectAsync();
            Connected?.Invoke(this, deviceName);
        }
        catch (Exception)
        {
            ErrorMessage = $"Couldn't connect to {deviceName}. Make sure the device is charged and within range.";
            Phase = ConnectionPhase.ConnectionFailed;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        Phase = DiscoveredDevices.Count > 0
            ? ConnectionPhase.DevicesFound
            : ConnectionPhase.PreConnect;
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        await ScanForDevicesAsync();
    }

    public void Reset()
    {
        _discoveryService.StopScan();
        Phase = ConnectionPhase.PreConnect;
        DiscoveredDevices.Clear();
        SelectedDevice = null;
        ConnectStep = 0;
        ErrorMessage = null;
    }

    private void OnDeviceFound(object? sender, DiscoveredDevice device)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DiscoveredDevices.Add(device);
        });
    }

    private void OnScanCompleted(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (Phase == ConnectionPhase.Scanning)
            {
                Phase = DiscoveredDevices.Count > 0
                    ? ConnectionPhase.DevicesFound
                    : ConnectionPhase.NoDevicesFound;
            }
        });
    }
}
