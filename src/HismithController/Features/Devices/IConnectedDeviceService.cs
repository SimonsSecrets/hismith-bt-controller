using HismithController.Services;

namespace HismithController.Devices;

public interface IConnectedDeviceService
{
    IDevice? CurrentDevice { get; }

    // True while the current device is the offline DemoDevice (no BLE link). Lets the UI
    // distinguish "exploring offline" from a real connection.
    bool IsDemoMode { get; }

    event EventHandler<IDevice?>? DeviceChanged;

    // Enter offline demo mode: sets CurrentDevice to a DemoDevice (AK Series profile, no BLE)
    // and fires DeviceChanged. No scan/connect/identify happens.
    void EnterDemoMode();

    // Step 1: open the BLE link to the chosen peripheral. No model resolution yet.
    Task ConnectAsync(DiscoveredDevice discovered, CancellationToken cancellationToken = default);

    // Step 2: read the product code from the connected device and resolve it to
    // a concrete IDevice. Throws IncompatibleDeviceException if the product code
    // is not recognised. On success, CurrentDevice is set and DeviceChanged fires.
    Task<IDevice> IdentifyDeviceAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
