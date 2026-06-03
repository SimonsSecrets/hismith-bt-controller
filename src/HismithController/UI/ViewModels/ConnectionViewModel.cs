using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Configuration;
using HismithController.Devices;
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
    IncompatibleDevice,
    BluetoothUnavailable
}

public partial class ConnectionViewModel : ObservableObject
{
    private readonly IDeviceDiscoveryService _discoveryService;
    private readonly IConnectedDeviceService _connectedDevice;
    private readonly AppSettings _settings;

    public bool IsMockMode => _settings.UseMockBle;

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

    [ObservableProperty]
    private string? _identifiedProductName;

    // Minimum visible duration for each step in the connect flow, so the UI
    // never strobes when the real work finishes almost instantly (e.g. mock mode).
    private static readonly TimeSpan MinStepDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdentifiedDisplayDuration = TimeSpan.FromSeconds(1);

    public event EventHandler<string>? Connected;

    public ConnectionViewModel(
        IDeviceDiscoveryService discoveryService,
        IConnectedDeviceService connectedDevice,
        AppSettings settings)
    {
        _discoveryService = discoveryService;
        _connectedDevice = connectedDevice;
        _settings = settings;

        _discoveryService.DeviceFound += OnDeviceFound;
        _discoveryService.ScanCompleted += OnScanCompleted;
        _discoveryService.AdapterUnavailable += OnAdapterUnavailable;
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
        IdentifiedProductName = null;

        Phase = ConnectionPhase.Connecting;

        // Step 1 — open the BLE link.
        ConnectStep = 1;
        try
        {
            await RunWithMinDuration(_connectedDevice.ConnectAsync(SelectedDevice), MinStepDuration);
        }
        catch (Exception)
        {
            ErrorMessage = $"Couldn't connect to {deviceName}. Make sure the device is charged and within range.";
            Phase = ConnectionPhase.ConnectionFailed;
            return;
        }

        // Step 2 — read product code and resolve model.
        ConnectStep = 2;
        IDevice device;
        try
        {
            device = await RunWithMinDuration(_connectedDevice.IdentifyDeviceAsync(), MinStepDuration);
        }
        catch (IncompatibleDeviceException)
        {
            Phase = ConnectionPhase.IncompatibleDevice;
            return;
        }
        catch (Exception)
        {
            ErrorMessage = $"Connected to {deviceName} but couldn't identify the model.";
            Phase = ConnectionPhase.ConnectionFailed;
            return;
        }

        // Both steps complete — mark step 2 done so the stepper shows green checks
        // while the identified product is displayed before handoff.
        ConnectStep = 3;

        // Show the identified product briefly before handing off to the connected view.
        IdentifiedProductName = device.DisplayName;
        await Task.Delay(IdentifiedDisplayDuration);

        Connected?.Invoke(this, deviceName);
    }

    private static async Task RunWithMinDuration(Task work, TimeSpan minimum)
    {
        var delay = Task.Delay(minimum);
        await work;
        await delay;
    }

    private static async Task<T> RunWithMinDuration<T>(Task<T> work, TimeSpan minimum)
    {
        var delay = Task.Delay(minimum);
        var result = await work;
        await delay;
        return result;
    }

    [RelayCommand]
    private async Task SkipToConnectedAsync()
    {
        var fake = new DiscoveredDevice("mock-skip", "HISMITH", "00:00:00:00:00:00", 3);
        await _connectedDevice.ConnectAsync(fake);
        await _connectedDevice.IdentifyDeviceAsync();
        Connected?.Invoke(this, "HISMITH-MOCK");
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

    [RelayCommand]
    private void OpenBluetoothSettings()
    {
        // The design's failAdapter shows a "Would open Windows Settings" toast (toast is SKIP, §4.1),
        // so we open the real Windows Bluetooth settings page instead.
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
    }

    public void Reset()
    {
        _discoveryService.StopScan();
        Phase = ConnectionPhase.PreConnect;
        DiscoveredDevices.Clear();
        SelectedDevice = null;
        ConnectStep = 0;
        ErrorMessage = null;
        IdentifiedProductName = null;
    }

    private void OnDeviceFound(object? sender, DiscoveredDevice device)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DiscoveredDevices.Add(device);
        });
    }

    private void OnAdapterUnavailable(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Phase = ConnectionPhase.BluetoothUnavailable;
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
