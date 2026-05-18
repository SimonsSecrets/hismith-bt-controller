using HismithController.Bluetooth;

namespace HismithController.Tests.Bluetooth;

public class HismithProtocolTests
{
    [Fact]
    public void PowerOn_ReturnsCorrectBytes()
    {
        var cmd = HismithProtocol.PowerOn();
        Assert.Equal([0xAA, 0x01, 0x00, 0x01], cmd);
    }

    [Fact]
    public void PowerOff_ReturnsCorrectBytes()
    {
        var cmd = HismithProtocol.PowerOff();
        Assert.Equal([0xAA, 0x02, 0x00, 0x02], cmd);
    }

    [Fact]
    public void SetSpeed_Zero_MatchesStop()
    {
        Assert.Equal(HismithProtocol.Stop(), HismithProtocol.SetSpeed(0));
    }

    [Fact]
    public void Stop_ReturnsCorrectBytes()
    {
        var cmd = HismithProtocol.Stop();
        Assert.Equal([0xAA, 0x04, 0x00, 0x04], cmd);
    }

    [Theory]
    [InlineData(0, new byte[] { 0xAA, 0x04, 0x00, 0x04 })]
    [InlineData(50, new byte[] { 0xAA, 0x04, 0x32, 0x36 })]
    [InlineData(100, new byte[] { 0xAA, 0x04, 0x64, 0x68 })]
    public void SetSpeed_ReturnsCorrectBytes(byte speed, byte[] expected)
    {
        Assert.Equal(expected, HismithProtocol.SetSpeed(speed));
    }

    [Fact]
    public void SetSpeed_Above100_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HismithProtocol.SetSpeed(101));
    }

    [Fact]
    public void ChecksumIsAdditive_NotXor()
    {
        // Speed 50: command=0x04, value=0x32 -> checksum should be 0x04+0x32=0x36
        // XOR would give 0x04^0x32=0x36 (same here!), so use speed 3 where they differ:
        // command=0x04, value=0x03 -> additive: 0x07, XOR: 0x07 (still same!)
        // Use speed 100: command=0x04, value=0x64 -> additive: 0x68, XOR: 0x60
        var cmd = HismithProtocol.SetSpeed(100);
        Assert.Equal(0x68, cmd[3]); // additive: 0x04 + 0x64 = 0x68
        Assert.NotEqual(0x60, cmd[3]); // XOR would be: 0x04 ^ 0x64 = 0x60
    }

    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)5)]
    [InlineData((byte)9)]
    public void SetMode_ValidModes_ReturnsCorrectFormat(byte mode)
    {
        var cmd = HismithProtocol.SetMode(mode);
        Assert.Equal(0xAA, cmd[0]);
        Assert.Equal(0x05, cmd[1]);
        Assert.Equal(mode, cmd[2]);
        Assert.Equal((byte)(0x05 + mode), cmd[3]);
    }

    [Fact]
    public void SetMode_Zero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HismithProtocol.SetMode(0));
    }

    [Fact]
    public void SetMode_Above9_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HismithProtocol.SetMode(10));
    }

    [Fact]
    public void AllCommands_HaveLength4()
    {
        Assert.Equal(4, HismithProtocol.PowerOn().Length);
        Assert.Equal(4, HismithProtocol.PowerOff().Length);
        Assert.Equal(4, HismithProtocol.Stop().Length);
        Assert.Equal(4, HismithProtocol.SetSpeed(50).Length);
        Assert.Equal(4, HismithProtocol.SetMode(1).Length);
    }

    [Fact]
    public void AllCommands_StartWithAaPrefix()
    {
        Assert.Equal(0xAA, HismithProtocol.PowerOn()[0]);
        Assert.Equal(0xAA, HismithProtocol.PowerOff()[0]);
        Assert.Equal(0xAA, HismithProtocol.Stop()[0]);
        Assert.Equal(0xAA, HismithProtocol.SetSpeed(50)[0]);
        Assert.Equal(0xAA, HismithProtocol.SetMode(1)[0]);
    }
}
