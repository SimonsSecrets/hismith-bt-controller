namespace HismithController.Devices;

public sealed class IncompatibleDeviceException : Exception
{
    public IncompatibleDeviceException(ushort productCode)
        : base($"Unsupported Hismith product code 0x{productCode:X4}.")
    {
        ProductCode = productCode;
    }

    public ushort ProductCode { get; }
}
