using HismithController.Services;

namespace HismithController.Bluetooth;

public interface IBleDeviceService : IAsyncDisposable
{
    BleConnectionState ConnectionState { get; }

    event EventHandler<BleDeviceStatus> StatusChanged;

    Task ConnectAsync(DiscoveredDevice device, CancellationToken cancellationToken = default);

    Task<ushort> GetProductCodeAsync(CancellationToken cancellationToken = default);

    Task SendSpeedAsync(byte speed, CancellationToken cancellationToken = default);

    Task PowerOnAsync(CancellationToken cancellationToken = default);

    Task PowerOffAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
