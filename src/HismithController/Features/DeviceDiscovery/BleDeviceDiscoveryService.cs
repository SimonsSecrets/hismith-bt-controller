using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;

namespace HismithController.Services;

public sealed class BleDeviceDiscoveryService : IDeviceDiscoveryService
{
    private static readonly TimeSpan ScanDuration = TimeSpan.FromSeconds(12);

    private readonly ILogger<BleDeviceDiscoveryService> _logger;
    private readonly HashSet<ulong> _seenAddresses = [];
    private readonly object _seenLock = new();

    private BluetoothLEAdvertisementWatcher? _watcher;
    private CancellationTokenSource? _scanCts;
    private bool _explicitlyStopped;

    public event EventHandler<DiscoveredDevice>? DeviceFound;
    public event EventHandler? ScanCompleted;

    public BleDeviceDiscoveryService(ILogger<BleDeviceDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task StartScanAsync(CancellationToken cancellationToken = default)
    {
        StopScan();

        lock (_seenLock)
            _seenAddresses.Clear();

        _explicitlyStopped = false;
        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _scanCts.Token;

        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped += OnWatcherStopped;

        try
        {
            _watcher.Start();
            _logger.LogInformation("BLE discovery scan started");
            await Task.Delay(ScanDuration, token);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by StopScan or external token
        }
        finally
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnWatcherStopped;
            _watcher.Stop();
            _watcher = null;

            if (!_explicitlyStopped)
                ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void StopScan()
    {
        _explicitlyStopped = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var name = args.Advertisement.LocalName;
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_seenLock)
        {
            if (!_seenAddresses.Add(args.BluetoothAddress))
                return;
        }

        var address = FormatAddress(args.BluetoothAddress);
        var signal = RssiToSignalStrength(args.RawSignalStrengthInDBm);
        var device = new DiscoveredDevice(address, name, address, signal);

        _logger.LogInformation("Found BLE device: {Name} ({Address}) RSSI={Rssi}",
            name, address, args.RawSignalStrengthInDBm);

        DeviceFound?.Invoke(this, device);
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error == BluetoothError.RadioNotAvailable)
        {
            _logger.LogError("Bluetooth radio is not available — cannot scan for devices");
            _scanCts?.Cancel();
        }
    }

    private static string FormatAddress(ulong address) =>
        $"{(address >> 40) & 0xFF:X2}:{(address >> 32) & 0xFF:X2}:{(address >> 24) & 0xFF:X2}:" +
        $"{(address >> 16) & 0xFF:X2}:{(address >> 8) & 0xFF:X2}:{address & 0xFF:X2}";

    private static int RssiToSignalStrength(short rssi) => rssi switch
    {
        >= -60 => 3,
        >= -75 => 2,
        _ => 1
    };
}
