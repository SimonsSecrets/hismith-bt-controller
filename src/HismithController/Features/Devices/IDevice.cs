namespace HismithController.Devices;

public interface IDevice : IAsyncDisposable
{
    string DisplayName { get; }

    int MaxBpm { get; }

    IReadOnlyList<SpeedPreset> Presets { get; }

    int BpmToPercent(int bpm);

    int PercentToBpm(int percent);

    Task SetTargetBpmAsync(int bpm, CancellationToken cancellationToken = default);

    Task PowerOnAsync(CancellationToken cancellationToken = default);

    Task PowerOffAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync();
}
