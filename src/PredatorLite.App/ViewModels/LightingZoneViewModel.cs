using CommunityToolkit.Mvvm.ComponentModel;

namespace PredatorLite.App.ViewModels;

public partial class LightingZoneViewModel : ObservableObject
{
    public LightingZoneViewModel(int index, string colorHex)
    {
        Index = index;
        color = colorHex;
    }

    public int Index { get; }

    [ObservableProperty]
    private string color;
}
