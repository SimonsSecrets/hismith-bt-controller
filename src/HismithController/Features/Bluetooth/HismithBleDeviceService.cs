using HismithController.Services;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HismithController.Bluetooth;

public sealed class HismithBleDeviceService : IBleDeviceService
{
    private readonly ILogger<HismithBleDeviceService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private BluetoothLEDevice? _device;
    private GattCharacteristic? _txCharacteristic;
    private BleConnectionState _connectionState = BleConnectionState.Disconnected;
    private int _connectionInProgress;

    public HismithBleDeviceService(ILogger<HismithBleDeviceService> logger)
    {
        _logger = logger;
    }

    public BleConnectionState ConnectionState => _connectionState;

    public event EventHandler<BleDeviceStatus>? StatusChanged;

    public async Task ConnectAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        if (_connectionState == BleConnectionState.Connected)
            return;

        if (Interlocked.CompareExchange(ref _connectionInProgress, 1, 0) != 0)
            throw new InvalidOperationException("A connection attempt is already in progress.");

        try
        {
            SetState(BleConnectionState.Connecting);
            var address = ParseBluetoothAddress(device.Address);
            _logger.LogInformation("Connecting to {Name} at {Address}", device.Name, device.Address);
            await ConnectToDeviceAsync(address);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to device");
            CleanupDeviceResources();
            SetState(BleConnectionState.Error, errorMessage: ex.Message);
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _connectionInProgress, 0);
        }
    }

    private static ulong ParseBluetoothAddress(string formatted)
    {
        var hex = formatted.Replace(":", string.Empty, StringComparison.Ordinal);
        return ulong.Parse(hex, System.Globalization.NumberStyles.HexNumber);
    }

    public async Task<ushort> GetProductCodeAsync(CancellationToken cancellationToken = default)
    {
        var device = _device
            ?? throw new InvalidOperationException("Not connected to device.");

        var servicesResult = await device.GetGattServicesForUuidAsync(
            HismithProtocol.InfoServiceUuid, BluetoothCacheMode.Uncached);

        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            throw new InvalidOperationException(
                $"Info service {HismithProtocol.InfoServiceUuid} not found (status: {servicesResult.Status}).");

        var service = servicesResult.Services[0];
        var charsResult = await service.GetCharacteristicsForUuidAsync(
            HismithProtocol.ModelCharacteristicUuid, BluetoothCacheMode.Uncached);

        if (charsResult.Status != GattCommunicationStatus.Success || charsResult.Characteristics.Count == 0)
            throw new InvalidOperationException(
                $"Model characteristic {HismithProtocol.ModelCharacteristicUuid} not found (status: {charsResult.Status}).");

        var readResult = await charsResult.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached);
        if (readResult.Status != GattCommunicationStatus.Success)
            throw new InvalidOperationException(
                $"Failed to read model characteristic (status: {readResult.Status}).");

        var reader = DataReader.FromBuffer(readResult.Value);
        var rawBytes = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(rawBytes);
        var rawHex = Convert.ToHexString(rawBytes);
        _logger.LogInformation("Model characteristic raw bytes: {Hex} ({Length} bytes)",
            rawHex, rawBytes.Length);

        if (rawBytes.Length < sizeof(ushort))
            throw new InvalidOperationException(
                $"Model characteristic returned {rawBytes.Length} bytes (expected ≥ 2). Raw: {rawHex}");

        var code = (ushort)((rawBytes[0] << 8) | rawBytes[1]);
        _logger.LogInformation("Device product code: 0x{Code:X4}", code);
        return code;
    }

    private async Task ConnectToDeviceAsync(ulong bluetoothAddress)
    {
        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(bluetoothAddress);
        if (_device is null)
            throw new InvalidOperationException("Could not create BLE device from address.");

        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        var servicesResult = await _device.GetGattServicesForUuidAsync(
            HismithProtocol.TxServiceUuid, BluetoothCacheMode.Uncached);

        if (servicesResult.Status != GattCommunicationStatus.Success || servicesResult.Services.Count == 0)
            throw new InvalidOperationException(
                $"Tx service {HismithProtocol.TxServiceUuid} not found (status: {servicesResult.Status}).");

        var service = servicesResult.Services[0];
        var charsResult = await service.GetCharacteristicsForUuidAsync(
            HismithProtocol.TxCharacteristicUuid, BluetoothCacheMode.Uncached);

        if (charsResult.Status != GattCommunicationStatus.Success || charsResult.Characteristics.Count == 0)
            throw new InvalidOperationException(
                $"Tx characteristic {HismithProtocol.TxCharacteristicUuid} not found (status: {charsResult.Status}).");

        _txCharacteristic = charsResult.Characteristics[0];

        SetState(BleConnectionState.Connected, _device.Name);
        _logger.LogInformation("Connected to {Name}", _device.Name);
    }

    public async Task SendSpeedAsync(byte speed, CancellationToken cancellationToken = default)
    {
        var characteristic = _txCharacteristic
            ?? throw new InvalidOperationException("Not connected to device.");

        var command = HismithProtocol.SetSpeed(speed);
        await WriteCommandAsync(characteristic, command, cancellationToken);
    }

    public async Task PowerOnAsync(CancellationToken cancellationToken = default)
    {
        var characteristic = _txCharacteristic
            ?? throw new InvalidOperationException("Not connected to device.");

        await WriteCommandAsync(characteristic, HismithProtocol.PowerOn(), cancellationToken);
    }

    public async Task PowerOffAsync(CancellationToken cancellationToken = default)
    {
        var characteristic = _txCharacteristic
            ?? throw new InvalidOperationException("Not connected to device.");

        await WriteCommandAsync(characteristic, HismithProtocol.PowerOff(), cancellationToken);
    }

    private async Task WriteCommandAsync(GattCharacteristic characteristic, byte[] command,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var writer = new DataWriter();
            writer.WriteBytes(command);
            var result = await characteristic.WriteValueWithResultAsync(
                writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);

            if (result.Status != GattCommunicationStatus.Success)
            {
                _logger.LogWarning("BLE write failed: {Status} (protocol error: {Error})",
                    result.Status, result.ProtocolError);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connectionState == BleConnectionState.Disconnected)
            return;

        try
        {
            if (_txCharacteristic is not null)
                await WriteCommandAsync(_txCharacteristic, HismithProtocol.Stop());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send stop command during disconnect");
        }

        CleanupDeviceResources();
        SetState(BleConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _writeLock.Dispose();
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            _logger.LogWarning("Device disconnected unexpectedly");
            CleanupDeviceResources();
            SetState(BleConnectionState.Disconnected,
                errorMessage: "Device disconnected unexpectedly.");
        }
    }

    private void CleanupDeviceResources()
    {
        _txCharacteristic = null;

        if (_device is not null)
        {
            _device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            _device.Dispose();
            _device = null;
        }
    }

    private void SetState(BleConnectionState state, string deviceName = "",
        string? errorMessage = null)
    {
        _connectionState = state;
        StatusChanged?.Invoke(this, new BleDeviceStatus(state, deviceName, errorMessage));
    }
}
