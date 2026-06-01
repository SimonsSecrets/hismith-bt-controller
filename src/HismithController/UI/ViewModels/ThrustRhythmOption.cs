using CommunityToolkit.Mvvm.ComponentModel;
using HismithController.SoundMode;

namespace HismithController.ViewModels;

// One tile in the thrust-rhythm selector. IsSelected mirrors the PresetItem.IsActive
// pattern: the view binds the active-tile highlight to it via a DataTrigger, and the
// view-model flips the flags when SelectedRhythm changes (see UpdateRhythmSelection).
public partial class ThrustRhythmOption : ObservableObject
{
    public ThrustRhythmOption(ThrustRhythm rhythm, string label, string description)
    {
        Rhythm = rhythm;
        Label = label;
        Description = description;
    }

    public ThrustRhythm Rhythm { get; }

    // The beats-per-stroke divider; bound by RhythmDiagram to draw the right wave.
    public int Ratio => (int)Rhythm;

    public string Label { get; }

    public string Description { get; }

    [ObservableProperty]
    private bool _isSelected;
}
