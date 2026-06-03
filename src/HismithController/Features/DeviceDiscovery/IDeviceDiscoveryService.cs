namespace HismithController.Services;

public interface IDeviceDiscoveryService
{
    event EventHandler<DiscoveredDevice>? DeviceFound;
    event EventHandler? ScanCompleted;

    /// <summary>Raised when a scan cannot run because the Bluetooth radio is unavailable.</summary>
    event EventHandler? AdapterUnavailable;

    Task StartScanAsync(CancellationToken cancellationToken = default);
    void StopScan();
}
