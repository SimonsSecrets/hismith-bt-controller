using HismithController.Bluetooth;

namespace HismithController.Devices;

public sealed class HismithDevice : IDevice
{
    private readonly IBleDeviceService _ble;

    public HismithDevice(
        string displayName,
        int maxBpm,
        IReadOnlyList<SpeedPreset> presets,
        IBleDeviceService ble)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBpm, 1);
        DisplayName = displayName;
        MaxBpm = maxBpm;
        Presets = presets;
        _ble = ble;
    }

    public string DisplayName { get; }

    public int MaxBpm { get; }

    public IReadOnlyList<SpeedPreset> Presets { get; }

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

    public static HismithDevice? CreateForProductCode(ushort productCode, IBleDeviceService ble) =>
        productCode switch
        {
            MockBleDeviceService.ProductCodeAkSeries
                => new HismithDevice("Hismith Pro 1 (AK Series)", 240, Pro1Presets, ble),
            MockBleDeviceService.ProductCodeMockMini
                => new HismithDevice("Hismith Mini (mock)", 100, MiniPresets, ble),
            _ => null,
        };

    // Advertisement-name gate used during discovery, before we can read the
    // model characteristic. Any Hismith-branded device passes; the concrete
    // model is decided post-connect from the product code.
    public static bool IsKnownAdvertisementName(string name) =>
        name.StartsWith("HISMITH", StringComparison.OrdinalIgnoreCase);
}
