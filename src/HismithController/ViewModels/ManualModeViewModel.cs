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
    private DispatcherTimer? _bleWriteDebounce;
    private DispatcherTimer? _pulseResetTimer;

    private double _rampPosition;
    private double _rampStep;
    private int _rampStart;
    private int _rampTarget;
    private int _rampDirection;

    private int _targetBpm;
    private bool _settingFromRamp;
    private bool _isDragging;
    private bool _bpmFieldFocused;
    private bool _percentFieldFocused;

    private int _lastBleSentBpm = -1;
    private int _lastBleSentTickMs;

    private const int UiTickMs = 16;
    private const int BleMinIntervalMs = 50;
    private const int RampMsPerBpm = 12;  // constant rate: ~83 BPM/sec

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedSpeedPercent))]
    private int _displayedBpm;

    [ObservableProperty]
    private int _maxBpm = 100;

    [ObservableProperty]
    private bool _isTargetReached;

    [ObservableProperty]
    private string _bpmFieldText = "0";

    [ObservableProperty]
    private string _percentFieldText = "0";

    public ObservableCollection<PresetItem> Presets { get; } = [];

    public int DisplayedSpeedPercent => Device?.BpmToPercent(DisplayedBpm) ?? 0;

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
        OnPropertyChanged(nameof(DisplayedSpeedPercent));
        RefreshFieldText();
    }

    partial void OnDisplayedBpmChanged(int oldValue, int newValue)
    {
        if (!_bpmFieldFocused)
            BpmFieldText = newValue.ToString();
        if (!_percentFieldFocused)
            PercentFieldText = DisplayedSpeedPercent.ToString();

        if (_settingFromRamp) return;

        if (_isDragging)
        {
            // Thumb drag — direct drive, no ramp.
            StopRamp();
            _targetBpm = newValue;
            UpdatePresetActiveStates();
            RestartBleDebounce();
        }
        else
        {
            // Slider track click (IsMoveToPointEnabled jump) — revert and ramp from old to new.
            _settingFromRamp = true;
            DisplayedBpm = oldValue;
            _settingFromRamp = false;
            _targetBpm = newValue;
            UpdatePresetActiveStates();
            BeginRamp(_targetBpm);
        }
    }

    public void NotifyDragging(bool dragging)
    {
        if (dragging)
        {
            // Cancel any in-flight ramp (e.g. from a preceding track click) so the drag drives directly.
            StopRamp();
            _isDragging = true;
        }
        else
        {
            _isDragging = false;
        }
    }

    public void SetTargetFromPercent(int percent)
    {
        if (Device is null) return;
        _targetBpm = Math.Clamp(Device.PercentToBpm(percent), 0, MaxBpm);
        UpdatePresetActiveStates();
        BeginRamp(_targetBpm);
    }

    public void SetTargetBpm(int bpm)
    {
        _targetBpm = Math.Clamp(bpm, 0, MaxBpm);
        UpdatePresetActiveStates();
        BeginRamp(_targetBpm);
    }

    [RelayCommand]
    private void SetPreset(PresetItem preset)
    {
        if (preset is null) return;
        _targetBpm = preset.Bpm;
        UpdatePresetActiveStates();
        BeginRamp(_targetBpm);
    }

    public void NotifyBpmFieldFocus(bool focused)
    {
        _bpmFieldFocused = focused;
        if (!focused)
            BpmFieldText = DisplayedBpm.ToString();
    }

    public void NotifyPercentFieldFocus(bool focused)
    {
        _percentFieldFocused = focused;
        if (!focused)
            PercentFieldText = DisplayedSpeedPercent.ToString();
    }

    public Task InitializeAsync()
    {
        StopRamp();
        _targetBpm = 0;
        _settingFromRamp = true;
        DisplayedBpm = 0;
        _settingFromRamp = false;
        _lastBleSentBpm = -1;
        IsTargetReached = false;
        ApplyDevice(Device);
        return Task.CompletedTask;
    }

    public void ForceStop()
    {
        StopRamp();
        _targetBpm = 0;
        _settingFromRamp = true;
        DisplayedBpm = 0;
        _settingFromRamp = false;
        _lastBleSentBpm = 0;
        _lastBleSentTickMs = Environment.TickCount;
        UpdatePresetActiveStates();
    }

    private void RefreshFieldText()
    {
        if (!_bpmFieldFocused)
            BpmFieldText = DisplayedBpm.ToString();
        if (!_percentFieldFocused)
            PercentFieldText = DisplayedSpeedPercent.ToString();
    }

    private void UpdatePresetActiveStates()
    {
        foreach (var preset in Presets)
            preset.IsActive = preset.Bpm == _targetBpm;
    }

    private void RestartBleDebounce()
    {
        if (_bleWriteDebounce == null)
        {
            _bleWriteDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _bleWriteDebounce.Tick += (_, _) =>
            {
                _bleWriteDebounce.Stop();
                if (Device is { } device)
                    _ = device.SetTargetBpmAsync(DisplayedBpm);
            };
        }
        _bleWriteDebounce.Stop();
        _bleWriteDebounce.Start();
    }

    private void BeginRamp(int newTarget)
    {
        newTarget = Math.Clamp(newTarget, 0, MaxBpm);
        StopRamp();

        _rampStart = DisplayedBpm;
        _rampTarget = newTarget;
        int delta = Math.Abs(newTarget - _rampStart);

        if (delta == 0)
        {
            TriggerPulse();
            return;
        }

        _rampDirection = Math.Sign(newTarget - _rampStart);
        _rampPosition = 0;

        // Constant rate: duration is proportional to delta.
        int durationMs = delta * RampMsPerBpm;
        int ticksNeeded = Math.Max(durationMs / UiTickMs, 1);
        _rampStep = (double)delta / ticksNeeded;

        _rampTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(UiTickMs) };
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

        _settingFromRamp = true;
        DisplayedBpm = newSpeed;
        _settingFromRamp = false;

        bool atTarget = newSpeed == _rampTarget;
        int nowMs = Environment.TickCount;
        bool dueByTime = nowMs - _lastBleSentTickMs >= BleMinIntervalMs;
        if (newSpeed != _lastBleSentBpm && (atTarget || dueByTime))
        {
            _lastBleSentBpm = newSpeed;
            _lastBleSentTickMs = nowMs;
            if (Device is { } device)
                _ = device.SetTargetBpmAsync(newSpeed);
        }

        if (atTarget)
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
