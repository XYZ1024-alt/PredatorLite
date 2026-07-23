using CommunityToolkit.Mvvm.ComponentModel;

namespace PredatorLite.App.ViewModels;

public partial class LightingZoneViewModel : ObservableObject
{
    public LightingZoneViewModel(int index, string colorHex, string zoneLabel)
    {
        Index = index;
        Color = colorHex;
        ZoneLabel = zoneLabel;
    }

    public int Index { get; }

    public string AutomationId => $"LightingZone.{Index}";

    public string AutomationName => $"{ZoneLabel} {Index}, {Color}";

    [ObservableProperty]
    public partial string Color { get; set; }

    [ObservableProperty]
    public partial string ZoneLabel { get; set; }

    partial void OnColorChanged(string value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnZoneLabelChanged(string value) => OnPropertyChanged(nameof(AutomationName));
}
