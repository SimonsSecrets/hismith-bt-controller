using Microsoft.Extensions.Logging;

namespace HismithController.Bluetooth;

public sealed class MockBleDeviceService : IBleDeviceService
{
    private readonly ILogger<MockBleDeviceService> _logger;
    private BleConnectionState _connectionState = BleConnectionState.Disconnected;

    public MockBleDeviceService(ILogger<MockBleDeviceService> logger)
    {
        _logger = logger;
    }

    public BleConnectionState ConnectionState => _connectionState;

    public event EventHandler<BleDeviceStatus>? StatusChanged;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        SetState(BleConnectionState.Scanning);
        await Task.Delay(500, cancellationToken);
        SetState(BleConnectionState.Connecting);
        await Task.Delay(300, cancellationToken);
        SetState(BleConnectionState.Connected, "HISMITH-MOCK");
        _logger.LogInformation("[MOCK] Connected to simulated device");
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

    private void SetState(BleConnectionState state, string deviceName = "HISMITH-MOCK",
        string? errorMessage = null)
    {
        _connectionState = state;
        StatusChanged?.Invoke(this, new BleDeviceStatus(state, deviceName, errorMessage));
    }
}
