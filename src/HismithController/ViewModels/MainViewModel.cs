using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Bluetooth;

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
    private readonly ConnectionViewModel _connectionViewModel;
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

    public ConnectionViewModel ConnectionViewModel => _connectionViewModel;

    public MainViewModel(
        IBleDeviceService bleService,
        ConnectionViewModel connectionViewModel)
    {
        _bleService = bleService;
        _connectionViewModel = connectionViewModel;
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
        await _bleService.DisconnectAsync();
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
            await _bleService.SendSpeedAsync(0);
        }
        catch
        {
            // Best-effort stop
        }

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

    private void OnDeviceConnected(object? sender, string deviceName)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            DeviceName = deviceName;
            IsConnected = true;
            ChipState = ChipState.Connected;
            ActiveMode = "Manual";
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
