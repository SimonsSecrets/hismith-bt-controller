using Microsoft.Extensions.Logging;

namespace HismithController.Devices;

// An IDevice for offline "demo mode": it mirrors a real Hismith (configured as the AK Series
// Pro 1) so every mode — Manual sliders, Sound BPM mapping, presets — behaves exactly as it
// would against hardware, but it never touches BLE. Speed/power commands are logged and
// discarded. This is deliberately separate from MockBleDeviceService (which simulates the BLE
// link and connect flow); DemoDevice represents an already-"connected" device with no link at all.
public sealed class DemoDevice : IDevice
{
    private readonly ILogger<DemoDevice> _logger;

    public DemoDevice(
        string displayName,
        int maxBpm,
        IReadOnlyList<SpeedPreset> presets,
        ILogger<DemoDevice> logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBpm, 1);
        DisplayName = displayName;
        MaxBpm = maxBpm;
        Presets = presets;
        _logger = logger;
    }

    public string DisplayName { get; }

    public int MaxBpm { get; }

    public IReadOnlyList<SpeedPreset> Presets { get; }

    // Same clamp/round mapping as HismithDevice so the demo slider tracks identically.
    public int BpmToPercent(int bpm)
    {
        var clamped = Math.Clamp(bpm, 0, MaxBpm);
        return (int)Math.Round(clamped * 100.0 / MaxBpm);
    }

    public int PercentToBpm(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        return (int)Math.Round(clamped * MaxBpm / 100.0);
    }

    public Task SetTargetBpmAsync(int bpm, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DEMO] SetTargetBpm({Bpm}) → {Percent}% (no signal sent)",
            bpm, BpmToPercent(bpm));
        return Task.CompletedTask;
    }

    public Task PowerOnAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DEMO] PowerOn (no signal sent)");
        return Task.CompletedTask;
    }

    public Task PowerOffAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[DEMO] PowerOff (no signal sent)");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
