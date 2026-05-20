namespace HismithController.Services;

public sealed record DiscoveredDevice(
    string Id,
    string Name,
    string Address,
    int SignalStrength);
