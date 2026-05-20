using HismithController.Services;
using Microsoft.Extensions.Logging;

namespace HismithController.Bluetooth;

public sealed class MockBleDeviceService : IBleDeviceService
{
    // Hismith AK Series (Pro 1) — verified with a real device on 2026-05-18.
    public const ushort ProductCodeAkSeries = 0x1001;

    // Made-up product code used only for the 100-BPM mock device, so the
    // product-code lookup path is exercised in mock mode.
    public const ushort ProductCodeMockMini = 0xFF01;

    // Returned for any mock peripheral that isn't a known Hismith model, so
    // the IncompatibleDevice path can be exercised in mock mode.
    public const ushort ProductCodeUnknown = 0x0000;

    private readonly ILogger<MockBleDeviceService> _logger;
    private BleConnectionState _connectionState = BleConnectionState.Disconnected;
    private DiscoveredDevice? _connectedDevice;

    public MockBleDeviceService(ILogger<MockBleDeviceService> logger)
    {
        _logger = logger;
    }

    public BleConnectionState ConnectionState => _connectionState;

    public event EventHandler<BleDeviceStatus>? StatusChanged;

    public async Task ConnectAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        SetState(BleConnectionState.Scanning);
        await Task.Delay(500, cancellationToken);
        SetState(BleConnectionState.Connecting);
        await Task.Delay(300, cancellationToken);
        _connectedDevice = device;
        SetState(BleConnectionState.Connected, device.Name);
        _logger.LogInformation("[MOCK] Connected to simulated device {Name}", device.Name);
    }

    public Task<ushort> GetProductCodeAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        var name = _connectedDevice?.Name ?? string.Empty;
        ushort code = name switch
        {
            _ when name.Equals("HISMITH", StringComparison.OrdinalIgnoreCase) => ProductCodeAkSeries,
            _ when name.Equals("HISMITH-MINI", StringComparison.OrdinalIgnoreCase) => ProductCodeMockMini,
            // Any other peripheral (e.g. the "Unknown device" mock entry) returns
            // a code the catalog doesn't recognise — surfaces as IncompatibleDevice.
            _ => ProductCodeUnknown,
        };
        _logger.LogInformation("[MOCK] Product code for {Name}: 0x{Code:X4}", name, code);
        return Task.FromResult(code);
    }

    public Task SendSpeedAsync(byte speed, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        _logger.LogInformation("[MOCK] SetSpeed({Speed}): {Hex}",
            speed, Convert.ToHexString(HismithProtocol.SetSpeed(speed)));
        return Task.CompletedTask;
    }

    public Task PowerOnAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] PowerOn: {Hex}",
            Convert.ToHexString(HismithProtocol.PowerOn()));
        return Task.CompletedTask;
    }

    public Task PowerOffAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[MOCK] PowerOff: {Hex}",
            Convert.ToHexString(HismithProtocol.PowerOff()));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _connectedDevice = null;
        SetState(BleConnectionState.Disconnected);
        _logger.LogInformation("[MOCK] Disconnected");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        SetState(BleConnectionState.Disconnected);
        return ValueTask.CompletedTask;
    }

    private void EnsureConnected()
    {
        if (_connectionState != BleConnectionState.Connected)
            throw new InvalidOperationException("Not connected to device.");
    }

    private void SetState(BleConnectionState state, string deviceName = "HISMITH",
        string? errorMessage = null)
    {
        _connectionState = state;
        StatusChanged?.Invoke(this, new BleDeviceStatus(state, deviceName, errorMessage));
    }
}
