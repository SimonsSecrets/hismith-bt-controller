using HismithController.Bluetooth;
using Microsoft.Extensions.Logging.Abstractions;

namespace HismithController.Tests.Bluetooth;

public class MockBleDeviceServiceTests
{
    private readonly MockBleDeviceService _sut = new(NullLogger<MockBleDeviceService>.Instance);

    [Fact]
    public async Task ConnectAsync_TransitionsThrough_Scanning_Connecting_Connected()
    {
        var states = new List<BleConnectionState>();
        _sut.StatusChanged += (_, status) => states.Add(status.State);

        await _sut.ConnectAsync();

        Assert.Equal(
            [BleConnectionState.Scanning, BleConnectionState.Connecting, BleConnectionState.Connected],
            states);
        Assert.Equal(BleConnectionState.Connected, _sut.ConnectionState);
    }

    [Fact]
    public async Task SendSpeedAsync_WhenNotConnected_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.SendSpeedAsync(50));
    }

    [Fact]
    public async Task SendSpeedAsync_WhenConnected_Succeeds()
    {
        await _sut.ConnectAsync();
        await _sut.SendSpeedAsync(50);
    }

    [Fact]
    public async Task DisconnectAsync_SetsDisconnected()
    {
        await _sut.ConnectAsync();
        await _sut.DisconnectAsync();

        Assert.Equal(BleConnectionState.Disconnected, _sut.ConnectionState);
    }

    [Fact]
    public async Task ConnectAsync_CancellationToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.ConnectAsync(cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_SetsDisconnected()
    {
        await _sut.ConnectAsync();
        await _sut.DisposeAsync();

        Assert.Equal(BleConnectionState.Disconnected, _sut.ConnectionState);
    }
}
