namespace HismithController.BeatDetection;

public sealed class BeatEventArgs : EventArgs
{
    // How far above the adaptive threshold the onset was:
    // 0 = just at threshold, 1 = 2× threshold (clamped to [0, 1]).
    public float Confidence { get; }
    public DateTimeOffset Timestamp { get; }

    public BeatEventArgs(DateTimeOffset timestamp, float confidence)
    {
        Timestamp  = timestamp;
        Confidence = confidence;
    }
}
