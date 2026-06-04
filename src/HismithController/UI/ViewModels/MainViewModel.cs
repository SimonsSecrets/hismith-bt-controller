using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Bluetooth;
using HismithController.Configuration;
using HismithController.Devices;

namespace HismithController.ViewModels;

public enum ChipState
{
    Disconnected,
    Scanning,
    Connecting,
    Connected,
    Demo,
    Lost
}

public partial class MainViewModel : ObservableObject
{
    private readonly IBleDeviceService _bleService;
    private readonly IConnectedDeviceService _connectedDevice;
    private readonly ConnectionViewModel _connectionViewModel;
    private readonly SoundModeViewModel _soundModeViewModel;
    private readonly UserPreferencesStore _prefsStore;
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

    // Drives the inline coral "Connection lost" banner in ConnectedView (UIDeviations §3.1).
    // Set when the BLE link drops while connected; cleared on Reconnect / Disconnect.
    [ObservableProperty]
    private bool _isConnectionLost;

    // App-level: when true the Settings screen overlays the whole window (replacing the
    // mode/connection content and hiding the footer), matching the design. Opened from the
    // connected mode-bar gear and the connection-screen FAB; both share this one flag.
    [ObservableProperty]
    private bool _isSettingsOpen;

    // App-level first-run welcome overlay (UIDeviations §2.1). Shown once over everything on the
    // first launch; "Get started" dismisses it and persists the flag so it never reappears.
    [ObservableProperty]
    private bool _isWelcomeOpen;

    // App-level "continue without a device?" warning overlay (design DemoNoticeDialog). Opened
    // from the pre-connect "Continue without connecting" link; blurs the window content behind it.
    [ObservableProperty]
    private bool _isDemoNoticeOpen;

    // True while exploring offline (no device connected). Drives the orange chip + demo popover.
    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private string _activeMode = "Manual";

    [ObservableProperty]
    private bool _isStopFlashing;

    [ObservableProperty]
    private object? _activeModeContent;

    public ConnectionViewModel ConnectionViewModel => _connectionViewModel;
    public ManualModeViewModel ManualModeViewModel { get; }

    // Exposed so the app-level Settings overlay (MainWindow.xaml) can bind its content.
    public SettingsViewModel SettingsViewModel { get; }

    public MainViewModel(
        IBleDeviceService bleService,
        IConnectedDeviceService connectedDevice,
        ConnectionViewModel connectionViewModel,
        ManualModeViewModel manualModeViewModel,
        SoundModeViewModel soundModeViewModel,
        SettingsViewModel settingsViewModel,
        UserPreferencesStore prefsStore)
    {
        _bleService = bleService;
        _connectedDevice = connectedDevice;
        _connectionViewModel = connectionViewModel;
        ManualModeViewModel = manualModeViewModel;
        _soundModeViewModel = soundModeViewModel;
        SettingsViewModel = settingsViewModel;
        _prefsStore = prefsStore;
        _currentView = connectionViewModel;

        // Gate the welcome overlay on the persisted first-run flag.
        _isWelcomeOpen = !prefsStore.Load().HasSeenWelcome;

        _connectionViewModel.Connected += OnDeviceConnected;
        _connectionViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConnectionViewModel.Phase))
                UpdateChipStateFromPhase();
        };

        _bleService.StatusChanged += OnBleStatusChanged;
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        IsSettingsOpen = true;
        // Opening Settings leaves the active mode, so release the device it was driving.
        await ReleaseActiveModeAsync();
    }

    [RelayCommand]
    private async Task CloseSettingsAsync()
    {
        IsSettingsOpen = false;
        // Re-arm the mode we're returning to (Sound restarts capture; Manual resets to 0).
        if (ActiveMode == "Sound")
            await _soundModeViewModel.InitializeAsync();
        else
            await ManualModeViewModel.InitializeAsync();
    }

    // Stops whatever the active mode was driving. Manual mode never sends its own stop
    // (ForceStop only zeroes the UI), and Sound mode owns audio capture, so handle both
    // and finish with an explicit BLE 0 to cover Manual mode's missing stop command.
    private async Task ReleaseActiveModeAsync()
    {
        if (ActiveMode == "Sound")
            await _soundModeViewModel.DeactivateAsync();   // stops capture + pushes 0
        ManualModeViewModel.ForceStop();
        try
        {
            if (_connectedDevice.CurrentDevice is { } device)
                await device.SetTargetBpmAsync(0);
        }
        catch
        {
            // Best-effort: the device may already be gone.
        }
    }

    [RelayCommand]
    private async Task ReconnectAsync()
    {
        IsConnectionLost = false;
        // Reuse the disconnect/reset path so we land back on the pre-connect scan screen.
        await DisconnectAsync();
    }

    [RelayCommand]
    private void DismissWelcome()
    {
        IsWelcomeOpen = false;
        // Load-modify-save so the Sound Mode / theme fields in the same file are untouched.
        _prefsStore.Update(p => p.HasSeenWelcome = true);
    }

    [RelayCommand]
    private void ShowDemoNotice()
    {
        IsDemoNoticeOpen = true;
    }

    [RelayCommand]
    private void CancelDemoNotice()
    {
        IsDemoNoticeOpen = false;
    }

    // Confirmed from the demo-notice dialog: enter offline exploration. Mirrors the connected
    // handoff (OnDeviceConnected) but backed by the no-BLE DemoDevice and flagged as demo.
    [RelayCommand]
    private void ConfirmDemoMode()
    {
        IsDemoNoticeOpen = false;
        _connectedDevice.EnterDemoMode();
        DeviceName = string.Empty;   // demo popover uses fixed copy, not DeviceName
        IsConnected = true;
        IsDemoMode = true;
        ChipState = ChipState.Demo;
        ActiveMode = "Manual";
        ActiveModeContent = ManualModeViewModel;
        _ = ManualModeViewModel.InitializeAsync();
        CurrentView = this;
    }

    // When the Popup (StaysOpen="False") auto-closes on an outside click, that same click on the
    // chip button then fires TogglePopover. Without a guard it would immediately reopen, so a
    // click while the popover is open never appears to close it. Record the close time and
    // suppress a reopen that lands within the click's settling window.
    private DateTime _popoverClosedAt = DateTime.MinValue;

    partial void OnIsPopoverOpenChanged(bool value)
    {
        if (!value)
            _popoverClosedAt = DateTime.UtcNow;
    }

    [RelayCommand]
    private void TogglePopover()
    {
        if (ChipState is not (ChipState.Connected or ChipState.Demo))
            return;

        if (!IsPopoverOpen && DateTime.UtcNow - _popoverClosedAt < TimeSpan.FromMilliseconds(250))
            return; // Popover was just dismissed by this same click; leave it closed.

        IsPopoverOpen = !IsPopoverOpen;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        IsPopoverOpen = false;
        IsConnectionLost = false;
        await _connectedDevice.DisconnectAsync();
        IsConnected = false;
        IsDemoMode = false;
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
        _soundModeViewModel.ForceStop();

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

    // Called before ActiveMode changes; releases the device the outgoing mode was driving
    // (ActiveMode still holds the outgoing mode here) so it never keeps running into the next mode.
    partial void OnActiveModeChanging(string value)
    {
        _ = ReleaseActiveModeAsync();
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
            _ => IsConnected
                ? (IsDemoMode ? ChipState.Demo : ChipState.Connected)
                : ChipState.Disconnected
        };
    }

    private void OnBleStatusChanged(object? sender, BleDeviceStatus status)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            // Demo mode never opened a BLE link, so a stray Disconnected must not raise the
            // lost-connection banner.
            if (status.State == BleConnectionState.Disconnected && IsConnected && !IsDemoMode)
            {
                ChipState = ChipState.Lost;
                IsConnectionLost = true;
                // Release both modes so neither keeps trying to drive a device that is gone
                // (no further BPM until the user reconnects). The inline coral banner
                // (IsConnectionLost) is the user-facing surface.
                _soundModeViewModel.ForceStop();
                ManualModeViewModel.ForceStop();
            }
        });
    }
}
