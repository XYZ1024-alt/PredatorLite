using CommunityToolkit.Mvvm.ComponentModel;

namespace PredatorLite.App.ViewModels;

public partial class LightingZoneViewModel : ObservableObject
{
    public LightingZoneViewModel(int index, string colorHex)
    {
        Index = index;
        Color = colorHex;
    }

    public int Index { get; }

    public string AutomationId => $"LightingZone.{Index}";

    [ObservableProperty]
    public partial string Color { get; set; }
}
