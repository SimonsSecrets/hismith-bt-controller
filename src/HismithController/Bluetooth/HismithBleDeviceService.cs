using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace HismithController.Bluetooth;

public sealed class HismithBleDeviceService : IBleDeviceService
{
    private readonly ILogger<HismithBleDeviceService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private BluetoothLEAdvertisementWatcher? _watcher;
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

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_connectionState == BleConnectionState.Connected)
            return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var ctReg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };

        SetState(BleConnectionState.Scanning);

        _watcher.Received += async (_, args) =>
        {
            if (tcs.Task.IsCompleted)
                return;

            var name = args.Advertisement.LocalName;
            if (!HismithProtocol.DeviceAdvertisementName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return;

            if (Interlocked.CompareExchange(ref _connectionInProgress, 1, 0) != 0)
                return;

            _watcher?.Stop();
            _logger.LogInformation("Found device {Name} at {Address:X12}", name, args.BluetoothAddress);

            try
            {
                SetState(BleConnectionState.Connecting);
                await ConnectToDeviceAsync(args.BluetoothAddress);
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to device");
                CleanupDeviceResources();
                SetState(BleConnectionState.Error, errorMessage: ex.Message);
                tcs.TrySetException(ex);
            }
            finally
            {
                Interlocked.Exchange(ref _connectionInProgress, 0);
            }
        };

        _watcher.Stopped += (_, args) =>
        {
            if (args.Error == BluetoothError.RadioNotAvailable)
            {
                var msg = "Bluetooth is not available. Enable Bluetooth in Windows Settings.";
                _logger.LogError(msg);
                SetState(BleConnectionState.Error, errorMessage: msg);
                tcs.TrySetException(new InvalidOperationException(msg));
            }
        };

        _watcher.Start();

        try
        {
            await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _watcher?.Stop();
            _watcher = null;
            Interlocked.Exchange(ref _connectionInProgress, 0);
            SetState(BleConnectionState.Disconnected);
            throw;
        }
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

        _watcher?.Stop();
        _watcher = null;
    }

    private void SetState(BleConnectionState state, string deviceName = "",
        string? errorMessage = null)
    {
        _connectionState = state;
        StatusChanged?.Invoke(this, new BleDeviceStatus(state, deviceName, errorMessage));
    }
}
