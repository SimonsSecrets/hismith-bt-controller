namespace HismithController.Services;

public sealed class MockDeviceDiscoveryService : IDeviceDiscoveryService
{
    private static readonly DiscoveredDevice[] MockDevices =
    [
        new("hi1", "HISMITH", "A4:C1:38:9F:21:E2", 3),
        new("hi2", "HISMITH-MINI", "A4:C1:38:7B:0C:18", 2),
        new("u1", "Unknown device", "5C:F3:70:11:8A:9D", 1),
    ];

    private CancellationTokenSource? _scanCts;

    public event EventHandler<DiscoveredDevice>? DeviceFound;
    public event EventHandler? ScanCompleted;

    public async Task StartScanAsync(CancellationToken cancellationToken = default)
    {
        StopScan();
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _scanCts.Token;

        foreach (var device in MockDevices)
        {
            await Task.Delay(700, token);
            if (token.IsCancellationRequested)
                return;
            DeviceFound?.Invoke(this, device);
        }

        ScanCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void StopScan()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }
}
