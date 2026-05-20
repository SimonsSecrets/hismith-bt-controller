namespace HismithController.Bluetooth;

public sealed record BleDeviceStatus(
    BleConnectionState State,
    string DeviceName,
    string? ErrorMessage = null);
