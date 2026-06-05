using HismithController.Bluetooth;
using Microsoft.Extensions.Logging;

namespace HismithController.Devices;

public sealed class HismithDevice : IDevice
{
    private readonly IBleDeviceService _ble;
    private readonly DeviceCalibration _calibration;

    public HismithDevice(
        string displayName,
        DeviceCalibration calibration,
        IReadOnlyList<SpeedPreset> presets,
        IBleDeviceService ble)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        DisplayName = displayName;
        _calibration = calibration;
        Presets = presets;
        _ble = ble;
    }

    public string DisplayName { get; }

    public int MaxBpm => _calibration.MaxBpm;

    public IReadOnlyList<SpeedPreset> Presets { get; }

    // Both directions delegate to the device-model calibration curve so the speed byte
    // sent to the hardware reflects its real (non-linear) tempo response — see §3.
    public int BpmToPercent(int bpm) => _calibration.BpmToPercent(bpm);

    public int PercentToBpm(int percent) => _calibration.PercentToBpm(percent);

    public Task SetTargetBpmAsync(int bpm, CancellationToken cancellationToken = default)
    {
        var pct = BpmToPercent(bpm);
        return _ble.SendSpeedAsync((byte)pct, cancellationToken);
    }

    public Task PowerOnAsync(CancellationToken cancellationToken = default) =>
        _ble.PowerOnAsync(cancellationToken);

    public Task PowerOffAsync(CancellationToken cancellationToken = default) =>
        _ble.PowerOffAsync(cancellationToken);

    public Task DisconnectAsync() => _ble.DisconnectAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public static class HismithDeviceCatalog
{
    private static readonly IReadOnlyList<SpeedPreset> Pro1Presets =
    [
        new("Lazy", 30),
        new("Gentle", 60),
        new("Pleasurable", 90),
        new("Intense", 120),
        new("Rough", 180),
        new("Destroyed", 240),
    ];

    private static readonly IReadOnlyList<SpeedPreset> MiniPresets =
    [
        new("Lazy", 10),
        new("Gentle", 25),
        new("Pleasurable", 40),
        new("Intense", 50),
        new("Rough", 75),
        new("Destroyed", 100),
    ];

    // Empirically measured speed→tempo response for the AK Series (Pro 1), per OpenPoints §3.
    // The device's tempo is sub-linear in the low range and saturates near the top, so a plain
    // linear percent scale overshoots the requested BPM. Shared by the real catalog entry and
    // the demo device so both describe the same hardware envelope; its top point (240) is the
    // Pro 1 ceiling that drives the app-wide BPM scale.
    private static readonly DeviceCalibration Pro1Calibration = new(
    [
        (0, 0), (10, 38), (20, 62), (30, 85), (40, 112), (50, 136),
        (60, 160), (70, 186), (80, 213), (90, 234), (100, 240),
    ]);

    // Demo device: a DemoDevice configured exactly like the AK Series (Pro 1) so every mode
    // behaves identically, but its BLE writes are no-ops (logged only). Used by the offline
    // "demo mode" exploration path, which never opens a real BLE link.
    public static DemoDevice CreateDemoDevice(ILogger<DemoDevice> logger) =>
        new("Hismith Pro 1 (AK Series) - DEMO", Pro1Calibration, Pro1Presets, logger);

    public static HismithDevice? CreateForProductCode(ushort productCode, IBleDeviceService ble) =>
        productCode switch
        {
            MockBleDeviceService.ProductCodeAkSeries
                => new HismithDevice("Hismith Pro 1 (AK Series)", Pro1Calibration, Pro1Presets, ble),
            // No measured curve for the Mini yet — fall back to a linear 0→100 BPM mapping.
            MockBleDeviceService.ProductCodeMockMini
                => new HismithDevice("Hismith Mini (mock)", DeviceCalibration.Linear(100), MiniPresets, ble),
            _ => null,
        };

    // Advertisement-name gate used during discovery, before we can read the
    // model characteristic. Any Hismith-branded device passes; the concrete
    // model is decided post-connect from the product code.
    public static bool IsKnownAdvertisementName(string name) =>
        name.StartsWith("HISMITH", StringComparison.OrdinalIgnoreCase);
}
