using CommunityToolkit.Mvvm.ComponentModel;

namespace HismithController.ViewModels;

public partial class PresetItem : ObservableObject
{
    public PresetItem(string name, int bpm)
    {
        Name = name;
        Bpm = bpm;
    }

    public string Name { get; }

    public int Bpm { get; }

    [ObservableProperty]
    private bool _isActive;
}
