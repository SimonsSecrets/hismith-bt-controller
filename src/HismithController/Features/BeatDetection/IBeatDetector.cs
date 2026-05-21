namespace HismithController.BeatDetection;

public interface IBeatDetector
{
    // Smoothed BPM derived from the last few inter-beat intervals; 0 before
    // at least 2 beats have been detected.
    int CurrentBpm { get; }

    // 0–1 confidence in the current BPM estimate; drops during tempo changes
    // and while the inter-beat interval history is still filling up.
    float Confidence { get; }

    // Fired synchronously on the audio capture thread — subscribers must not
    // block and must marshal any UI or BLE work to the appropriate thread.
    event EventHandler<BeatEventArgs>? BeatDetected;
}
