using HismithController.Bluetooth;
using HismithController.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace HismithController.Tests.Bluetooth;

public class MockBleDeviceServiceTests
{
    private readonly MockBleDeviceService _sut = new(NullLogger<MockBleDeviceService>.Instance);

    private static readonly DiscoveredDevice ProDevice =
        new("hi1", "HISMITH", "A4:C1:38:9F:21:E2", 3);

    private static readonly DiscoveredDevice MiniDevice =
        new("hi2", "HISMITH-MINI", "A4:C1:38:7B:0C:18", 2);

    [Fact]
    public async Task ConnectAsync_TransitionsThrough_Scanning_Connecting_Connected()
    {
        var states = new List<BleConnectionState>();
        _sut.StatusChanged += (_, status) => states.Add(status.State);

        await _sut.ConnectAsync(ProDevice);

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
        await _sut.ConnectAsync(ProDevice);
        await _sut.SendSpeedAsync(50);
    }

    [Fact]
    public async Task DisconnectAsync_SetsDisconnected()
    {
        await _sut.ConnectAsync(ProDevice);
        await _sut.DisconnectAsync();

        Assert.Equal(BleConnectionState.Disconnected, _sut.ConnectionState);
    }

    [Fact]
    public async Task ConnectAsync_CancellationToken_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.ConnectAsync(ProDevice, cts.Token));
    }

    [Fact]
    public async Task DisposeAsync_SetsDisconnected()
    {
        await _sut.ConnectAsync(ProDevice);
        await _sut.DisposeAsync();

        Assert.Equal(BleConnectionState.Disconnected, _sut.ConnectionState);
    }

    [Fact]
    public async Task GetProductCodeAsync_ProDevice_ReturnsAkSeriesCode()
    {
        await _sut.ConnectAsync(ProDevice);
        var code = await _sut.GetProductCodeAsync();
        Assert.Equal(MockBleDeviceService.ProductCodeAkSeries, code);
    }

    [Fact]
    public async Task GetProductCodeAsync_MiniDevice_ReturnsMockMiniCode()
    {
        await _sut.ConnectAsync(MiniDevice);
        var code = await _sut.GetProductCodeAsync();
        Assert.Equal(MockBleDeviceService.ProductCodeMockMini, code);
    }

    [Fact]
    public async Task GetProductCodeAsync_UnknownDevice_ReturnsUnknownCode()
    {
        var unknown = new DiscoveredDevice("u1", "Unknown device", "5C:F3:70:11:8A:9D", 1);
        await _sut.ConnectAsync(unknown);
        var code = await _sut.GetProductCodeAsync();
        Assert.Equal(MockBleDeviceService.ProductCodeUnknown, code);
    }
}
