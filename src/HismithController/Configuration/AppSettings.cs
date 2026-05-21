namespace HismithController.Configuration;

public sealed class AppSettings
{
    public bool UseMockBle { get; set; }
    public bool UseMockAudio { get; set; }

    // Onset multiplier for adaptive spectral-flux threshold: threshold = mean(flux history) × value.
    // Higher values require a more prominent transient to trigger a beat; lower values are more sensitive.
    public double OnsetMultiplier { get; set; } = 1.5;
}
