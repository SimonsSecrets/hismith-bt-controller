namespace HismithController.Bluetooth;

public interface IBleDeviceService : IAsyncDisposable
{
    BleConnectionState ConnectionState { get; }

    event EventHandler<BleDeviceStatus> StatusChanged;

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task SendSpeedAsync(byte speed, CancellationToken cancellationToken = default);

    Task PowerOnAsync(CancellationToken cancellationToken = default);

    Task PowerOffAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
