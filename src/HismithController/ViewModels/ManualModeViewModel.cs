using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HismithController.Bluetooth;

namespace HismithController.ViewModels;

public partial class ManualModeViewModel : ObservableObject
{
    private readonly IBleDeviceService _bleService;

    private DispatcherTimer? _rampTimer;
    private DispatcherTimer? _debounceTimer;
    private DispatcherTimer? _pulseResetTimer;

    private double _rampPosition;
    private double _rampStep;
    private int _rampStart;
    private int _rampTarget;
    private int _rampDirection;

    private bool _syncingUnits;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLazyPreset))]
    [NotifyPropertyChangedFor(nameof(IsGentlePreset))]
    [NotifyPropertyChangedFor(nameof(IsPleasurablePreset))]
    [NotifyPropertyChangedFor(nameof(IsIntensePreset))]
    [NotifyPropertyChangedFor(nameof(IsRoughPreset))]
    [NotifyPropertyChangedFor(nameof(IsDestroyedPreset))]
    private int _targetSpeedPercent;

    public bool IsLazyPreset => TargetSpeedPercent == 10;
    public bool IsGentlePreset => TargetSpeedPercent == 25;
    public bool IsPleasurablePreset => TargetSpeedPercent == 40;
    public bool IsIntensePreset => TargetSpeedPercent == 50;
    public bool IsRoughPreset => TargetSpeedPercent == 75;
    public bool IsDestroyedPreset => TargetSpeedPercent == 100;

    [ObservableProperty]
    private int _targetSpeedBpm;

    [ObservableProperty]
    private int _currentSpeedPercent;

    [ObservableProperty]
    private bool _isRamping;

    [ObservableProperty]
    private bool _isTargetReached;

    public ManualModeViewModel(IBleDeviceService bleService)
    {
        _bleService = bleService;
    }

    partial void OnTargetSpeedPercentChanged(int value)
    {
        if (_syncingUnits) return;
        _syncingUnits = true;
        TargetSpeedBpm = value * 240 / 100;
        _syncingUnits = false;
        RestartDebounce();
    }

    partial void OnTargetSpeedBpmChanged(int value)
    {
        if (_syncingUnits) return;
        _syncingUnits = true;
        TargetSpeedPercent = value * 100 / 240;
        _syncingUnits = false;
        RestartDebounce();
    }

    [RelayCommand]
    private void SetPreset(string pctStr)
    {
        if (int.TryParse(pctStr, out int pct))
            BeginRamp(pct);
    }

    public Task InitializeAsync()
    {
        StopRamp();
        _syncingUnits = true;
        TargetSpeedPercent = 0;
        TargetSpeedBpm = 0;
        CurrentSpeedPercent = 0;
        _syncingUnits = false;
        IsRamping = false;
        IsTargetReached = false;
        return Task.CompletedTask;
    }

    // Called from code-behind on slider DragCompleted — bypasses debounce.
    public void CommitSliderValue()
    {
        _debounceTimer?.Stop();
        BeginRamp(TargetSpeedPercent);
    }

    // Called by MainViewModel emergency stop.
    public void ForceStop()
    {
        StopRamp();
        _syncingUnits = true;
        TargetSpeedPercent = 0;
        TargetSpeedBpm = 0;
        CurrentSpeedPercent = 0;
        _syncingUnits = false;
    }

    private void RestartDebounce()
    {
        if (_debounceTimer == null)
        {
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                BeginRamp(TargetSpeedPercent);
            };
        }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void BeginRamp(int newTarget)
    {
        newTarget = Math.Clamp(newTarget, 0, 100);
        StopRamp();

        _rampStart = CurrentSpeedPercent;
        _rampTarget = newTarget;
        int delta = Math.Abs(newTarget - _rampStart);

        if (delta == 0)
        {
            TriggerPulse();
            return;
        }

        _rampDirection = Math.Sign(newTarget - _rampStart);
        _rampPosition = 0;

        int durationMs = Math.Clamp(delta * 12, 500, 1500);
        int ticksNeeded = durationMs / 50;
        _rampStep = (double)delta / ticksNeeded;

        IsRamping = true;
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

        CurrentSpeedPercent = newSpeed;
        _ = _bleService.SendSpeedAsync((byte)newSpeed);

        if (newSpeed == _rampTarget)
        {
            StopRamp();
            IsRamping = false;
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
