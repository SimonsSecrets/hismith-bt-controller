using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Devices;

namespace HismithController.ViewModels;

public partial class ManualModeViewModel : ObservableObject
{
    private readonly IConnectedDeviceService _connectedDevice;

    private DispatcherTimer? _rampTimer;
    private DispatcherTimer? _debounceTimer;
    private DispatcherTimer? _pulseResetTimer;

    private double _rampPosition;
    private double _rampStep;
    private int _rampStart;
    private int _rampTarget;
    private int _rampDirection;

    private int _currentSpeedBpm;

    private bool _syncingUnits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetSpeedPercent))]
    private int _targetSpeedBpm;

    [ObservableProperty]
    private int _maxBpm = 100;

    [ObservableProperty]
    private bool _isTargetReached;

    public ObservableCollection<PresetItem> Presets { get; } = [];

    public int TargetSpeedPercent => Device?.BpmToPercent(TargetSpeedBpm) ?? 0;

    private IDevice? Device => _connectedDevice.CurrentDevice;

    public ManualModeViewModel(IConnectedDeviceService connectedDevice)
    {
        _connectedDevice = connectedDevice;
        _connectedDevice.DeviceChanged += (_, device) => ApplyDevice(device);
        ApplyDevice(_connectedDevice.CurrentDevice);
    }

    private void ApplyDevice(IDevice? device)
    {
        Presets.Clear();
        if (device is not null)
        {
            MaxBpm = device.MaxBpm;
            foreach (var preset in device.Presets)
                Presets.Add(new PresetItem(preset.Name, preset.Bpm));
        }
        else
        {
            MaxBpm = 100;
        }
        UpdatePresetActiveStates();
        OnPropertyChanged(nameof(TargetSpeedPercent));
    }

    partial void OnTargetSpeedBpmChanged(int value)
    {
        if (_syncingUnits) return;
        UpdatePresetActiveStates();
        RestartDebounce();
    }

    // Allow the % textbox to write back to BPM via the device's mapping.
    public void SetTargetFromPercent(int percent)
    {
        if (Device is null) return;
        _syncingUnits = true;
        TargetSpeedBpm = Device.PercentToBpm(percent);
        _syncingUnits = false;
        UpdatePresetActiveStates();
        RestartDebounce();
    }

    [RelayCommand]
    private void SetPreset(PresetItem preset)
    {
        if (preset is null) return;
        _syncingUnits = true;
        TargetSpeedBpm = preset.Bpm;
        _syncingUnits = false;
        UpdatePresetActiveStates();
        _debounceTimer?.Stop();
        BeginRamp(preset.Bpm);
    }

    public Task InitializeAsync()
    {
        StopRamp();
        _syncingUnits = true;
        TargetSpeedBpm = 0;
        _currentSpeedBpm = 0;
        _syncingUnits = false;
        IsTargetReached = false;
        ApplyDevice(Device);
        return Task.CompletedTask;
    }

    // Called from code-behind on slider DragCompleted — bypasses debounce.
    public void CommitSliderValue()
    {
        _debounceTimer?.Stop();
        BeginRamp(TargetSpeedBpm);
    }

    // Called by MainViewModel emergency stop.
    public void ForceStop()
    {
        StopRamp();
        _syncingUnits = true;
        TargetSpeedBpm = 0;
        _currentSpeedBpm = 0;
        _syncingUnits = false;
        UpdatePresetActiveStates();
    }

    private void UpdatePresetActiveStates()
    {
        foreach (var preset in Presets)
            preset.IsActive = preset.Bpm == TargetSpeedBpm;
    }

    private void RestartDebounce()
    {
        if (_debounceTimer == null)
        {
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                BeginRamp(TargetSpeedBpm);
            };
        }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void BeginRamp(int newTarget)
    {
        newTarget = Math.Clamp(newTarget, 0, MaxBpm);
        StopRamp();

        _rampStart = _currentSpeedBpm;
        _rampTarget = newTarget;
        int delta = Math.Abs(newTarget - _rampStart);

        if (delta == 0)
        {
            TriggerPulse();
            return;
        }

        _rampDirection = Math.Sign(newTarget - _rampStart);
        _rampPosition = 0;

        // Scale ramp duration relative to MaxBpm so it feels the same across devices.
        int durationMs = Math.Clamp(delta * 1200 / Math.Max(MaxBpm, 1), 500, 1500);
        int ticksNeeded = Math.Max(durationMs / 50, 1);
        _rampStep = (double)delta / ticksNeeded;

        _rampTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _rampTimer.Tick += OnRampTick;
        _rampTimer.Start();
    }

    private void OnRampTick(object? sender, EventArgs e)
    {
        _rampPosition += _rampStep;
        int newSpeed = _rampStart + (int)Math.Round(_rampPosition) * _rampDirection;
        newSpeed = Math.Clamp(newSpeed,
            Math.Min(_rampStart, _rampTarget),
            Math.Max(_rampStart, _rampTarget));

        _currentSpeedBpm = newSpeed;
        if (Device is { } device)
            _ = device.SetTargetBpmAsync(newSpeed);

        if (newSpeed == _rampTarget)
        {
            StopRamp();
            TriggerPulse();
        }
    }

    private void TriggerPulse()
    {
        IsTargetReached = true;
        _pulseResetTimer?.Stop();
        _pulseResetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _pulseResetTimer.Tick += (_, _) =>
        {
            IsTargetReached = false;
            _pulseResetTimer?.Stop();
        };
        _pulseResetTimer.Start();
    }

    private void StopRamp()
    {
        _rampTimer?.Stop();
        _rampTimer = null;
    }
}
