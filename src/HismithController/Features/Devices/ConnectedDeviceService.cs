using HismithController.Bluetooth;
using HismithController.Services;

namespace HismithController.Devices;

public sealed class ConnectedDeviceService : IConnectedDeviceService
{
    private readonly IBleDeviceService _ble;

    public ConnectedDeviceService(IBleDeviceService ble)
    {
        _ble = ble;
    }

    public IDevice? CurrentDevice { get; private set; }

    public event EventHandler<IDevice?>? DeviceChanged;

    public Task ConnectAsync(DiscoveredDevice discovered, CancellationToken cancellationToken = default) =>
        _ble.ConnectAsync(discovered, cancellationToken);

    public async Task<IDevice> IdentifyDeviceAsync(CancellationToken cancellationToken = default)
    {
        var productCode = await _ble.GetProductCodeAsync(cancellationToken);
        var device = HismithDeviceCatalog.CreateForProductCode(productCode, _ble);
        if (device is null)
        {
            await _ble.DisconnectAsync();
            throw new IncompatibleDeviceException(productCode);
        }

        CurrentDevice = device;
        DeviceChanged?.Invoke(this, CurrentDevice);
        return device;
    }

    public async Task DisconnectAsync()
    {
        if (CurrentDevice is not null)
        {
            await CurrentDevice.DisconnectAsync();
            await CurrentDevice.DisposeAsync();
            CurrentDevice = null;
            DeviceChanged?.Invoke(this, null);
        }
        else
        {
            await _ble.DisconnectAsync();
        }
    }
}
