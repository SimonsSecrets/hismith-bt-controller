namespace HismithController.Bluetooth;

public static class HismithProtocol
{
    public const string DeviceAdvertisementName = "HISMITH";

    public static readonly Guid TxServiceUuid =
        Guid.Parse("0000ffe5-0000-1000-8000-00805f9b34fb");

    public static readonly Guid TxCharacteristicUuid =
        Guid.Parse("0000ffe9-0000-1000-8000-00805f9b34fb");

    public static readonly Guid RxServiceUuid =
        Guid.Parse("0000ffe0-0000-1000-8000-00805f9b34fb");

    public static readonly Guid RxCharacteristicUuid =
        Guid.Parse("0000ffe4-0000-1000-8000-00805f9b34fb");

    public static readonly Guid InfoServiceUuid =
        Guid.Parse("0000ff90-0000-1000-8000-00805f9b34fb");

    public static readonly Guid ModelCharacteristicUuid =
        Guid.Parse("0000ff96-0000-1000-8000-00805f9b34fb");

    public const byte MinSpeed = 0x00;
    public const byte MaxSpeed = 0x64;

    private const byte AaPrefix = 0xAA;

    private static byte Checksum(byte command, byte value) =>
        (byte)(command + value);

    public static byte[] PowerOn() =>
        [AaPrefix, 0x01, 0x00, Checksum(0x01, 0x00)];

    public static byte[] PowerOff() =>
        [AaPrefix, 0x02, 0x00, Checksum(0x02, 0x00)];

    public static byte[] SetSpeed(byte speed)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(speed, MaxSpeed);
        return [AaPrefix, 0x04, speed, Checksum(0x04, speed)];
    }

    public static byte[] Stop() => SetSpeed(0x00);

    public static byte[] SetMode(byte mode)
    {
        ArgumentOutOfRangeException.ThrowIfZero(mode);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(mode, (byte)0x09);
        return [AaPrefix, 0x05, mode, Checksum(0x05, mode)];
    }
}
