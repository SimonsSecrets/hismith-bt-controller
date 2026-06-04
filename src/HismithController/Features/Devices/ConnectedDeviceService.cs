using HismithController.Bluetooth;
using HismithController.Services;
using Microsoft.Extensions.Logging;

namespace HismithController.Devices;

public sealed class ConnectedDeviceService : IConnectedDeviceService
{
    private readonly IBleDeviceService _ble;
    private readonly ILogger<DemoDevice> _demoLogger;

    public ConnectedDeviceService(IBleDeviceService ble, ILogger<DemoDevice> demoLogger)
    {
        _ble = ble;
        _demoLogger = demoLogger;
    }

    public IDevice? CurrentDevice { get; private set; }

    public bool IsDemoMode { get; private set; }

    public event EventHandler<IDevice?>? DeviceChanged;

    public void EnterDemoMode()
    {
        CurrentDevice = HismithDeviceCatalog.CreateDemoDevice(_demoLogger);
        IsDemoMode = true;
        DeviceChanged?.Invoke(this, CurrentDevice);
    }

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
        IsDemoMode = false;
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
