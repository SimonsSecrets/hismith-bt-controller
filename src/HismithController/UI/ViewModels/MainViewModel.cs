using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Bluetooth;
using HismithController.Devices;

namespace HismithController.ViewModels;

public enum ChipState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
    Lost
}

public partial class MainViewModel : ObservableObject
{
    private readonly IBleDeviceService _bleService;
    private readonly IConnectedDeviceService _connectedDevice;
    private readonly ConnectionViewModel _connectionViewModel;
    private readonly SoundModeViewModel _soundModeViewModel;
    private DispatcherTimer? _stopFlashTimer;

    [ObservableProperty]
    private object _currentView;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    private ChipState _chipState = ChipState.Disconnected;

    [ObservableProperty]
    private bool _isPopoverOpen;

    [ObservableProperty]
    private string _activeMode = "Manual";

    [ObservableProperty]
    private bool _isStopFlashing;

    [ObservableProperty]
    private object? _activeModeContent;

    public ConnectionViewModel ConnectionViewModel => _connectionViewModel;
    public ManualModeViewModel ManualModeViewModel { get; }

    public MainViewModel(
        IBleDeviceService bleService,
        IConnectedDeviceService connectedDevice,
        ConnectionViewModel connectionViewModel,
        ManualModeViewModel manualModeViewModel,
        SoundModeViewModel soundModeViewModel)
    {
        _bleService = bleService;
        _connectedDevice = connectedDevice;
        _connectionViewModel = connectionViewModel;
        ManualModeViewModel = manualModeViewModel;
        _soundModeViewModel = soundModeViewModel;
        _currentView = connectionViewModel;

        _connectionViewModel.Connected += OnDeviceConnected;
        _connectionViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionViewModel.Phase))
                UpdateChipStateFromPhase();
        };

        _bleService.StatusChanged += OnBleStatusChanged;
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
        if (Application.Current is App app)
            app.ApplyTheme(IsDarkTheme);
    }

    [RelayCommand]
    private void TogglePopover()
    {
        if (ChipState == ChipState.Connected)
            IsPopoverOpen = !IsPopoverOpen;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        IsPopoverOpen = false;
        await _connectedDevice.DisconnectAsync();
        IsConnected = false;
        DeviceName = string.Empty;
        ChipState = ChipState.Disconnected;
        _connectionViewModel.Reset();
        CurrentView = _connectionViewModel;
    }

    [RelayCommand]
    private async Task EmergencyStopAsync()
    {
        if (!IsConnected)
            return;

        try
        {
            if (_connectedDevice.CurrentDevice is { } device)
                await device.SetTargetBpmAsync(0);
        }
        catch
        {
            // Best-effort stop
        }

        ManualModeViewModel.ForceStop();

        IsStopFlashing = true;
        _stopFlashTimer?.Stop();
        _stopFlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _stopFlashTimer.Tick += (_, _) =>
        {
            IsStopFlashing = false;
            _stopFlashTimer.Stop();
        };
        _stopFlashTimer.Start();
    }

    [RelayCommand]
    private void SwitchMode(string mode)
    {
        ActiveMode = mode;
    }

    // Called before ActiveMode changes; stops the outgoing mode's capture/ramp.
    partial void OnActiveModeChanging(string value)
    {
        if (ActiveMode == "Sound")
            _ = _soundModeViewModel.DeactivateAsync();
    }

    partial void OnActiveModeChanged(string value)
    {
        if (value == "Manual")
        {
            ActiveModeContent = ManualModeViewModel;
            _ = ManualModeViewModel.InitializeAsync();
        }
        else if (value == "Sound")
        {
            ActiveModeContent = _soundModeViewModel;
            _ = _soundModeViewModel.InitializeAsync();
        }
        else
        {
            ActiveModeContent = null;
        }
    }

    private void OnDeviceConnected(object? sender, string deviceName)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DeviceName = deviceName;
            IsConnected = true;
            ChipState = ChipState.Connected;
            ActiveMode = "Manual";
            // Explicitly set content in case ActiveMode didn't change (already "Manual")
            ActiveModeContent = ManualModeViewModel;
            _ = ManualModeViewModel.InitializeAsync();
            CurrentView = this;
        });
    }

    private void UpdateChipStateFromPhase()
    {
        ChipState = _connectionViewModel.Phase switch
        {
            ConnectionPhase.Scanning => ChipState.Scanning,
            ConnectionPhase.Connecting => ChipState.Connecting,
            _ => IsConnected ? ChipState.Connected : ChipState.Disconnected
        };
    }

    private void OnBleStatusChanged(object? sender, BleDeviceStatus status)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (status.State == BleConnectionState.Disconnected && IsConnected)
            {
                ChipState = ChipState.Lost;
            }
        });
    }
}
