using HismithController.Services;

namespace HismithController.Tests.Bluetooth;

public class BleLogTests
{
    [Fact]
    public void RedactAddress_MasksAllButLastOctet()
    {
        Assert.Equal("**:**:**:**:**:F6", BleLog.RedactAddress("A1:B2:C3:D4:E5:F6"));
    }

    [Fact]
    public void RedactAddress_KeepsLastOctetVerbatim_ForCorrelation()
    {
        // The retained octet must survive untouched so a "found" log and a later "connecting"
        // log for the same device can be matched.
        Assert.EndsWith(":0A", BleLog.RedactAddress("11:22:33:44:55:0A"));
    }

    [Fact]
    public void RedactAddress_RevealsNothing_WhenFormatUnexpected()
    {
        Assert.Equal("**", BleLog.RedactAddress("not-an-address"));
    }

    [Fact]
    public void RedactName_KeepsOnlyFirstCharacter()
    {
        Assert.Equal("H***", BleLog.RedactName("HISMITH"));
    }

    [Fact]
    public void RedactName_DoesNotPreserveLength()
    {
        // Two different personalized names of different lengths must redact identically beyond
        // the first character, so the masked form leaks nothing about length.
        Assert.Equal(BleLog.RedactName("Jo"), BleLog.RedactName("Jonathan"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RedactName_HandlesMissingName(string? name)
    {
        Assert.Equal("(unknown)", BleLog.RedactName(name));
    }
}
