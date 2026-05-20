namespace HismithController.Services;

public interface IDeviceDiscoveryService
{
    event EventHandler<DiscoveredDevice>? DeviceFound;
    event EventHandler? ScanCompleted;

    Task StartScanAsync(CancellationToken cancellationToken = default);
    void StopScan();
}
