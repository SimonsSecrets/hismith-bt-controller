using HismithController.Services;

namespace HismithController.Devices;

public interface IConnectedDeviceService
{
    IDevice? CurrentDevice { get; }

    event EventHandler<IDevice?>? DeviceChanged;

    // Step 1: open the BLE link to the chosen peripheral. No model resolution yet.
    Task ConnectAsync(DiscoveredDevice discovered, CancellationToken cancellationToken = default);

    // Step 2: read the product code from the connected device and resolve it to
    // a concrete IDevice. Throws IncompatibleDeviceException if the product code
    // is not recognised. On success, CurrentDevice is set and DeviceChanged fires.
    Task<IDevice> IdentifyDeviceAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
