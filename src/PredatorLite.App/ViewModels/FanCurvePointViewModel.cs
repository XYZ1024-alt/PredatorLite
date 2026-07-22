using CommunityToolkit.Mvvm.ComponentModel;
using PredatorLite.Core.Models;

namespace PredatorLite.App.ViewModels;

public partial class FanCurvePointViewModel : ObservableObject
{
    public FanCurvePointViewModel(FanCurvePoint point)
    {
        TemperatureC = point.TemperatureC;
        SpeedPercent = point.SpeedPercent;
    }

    [ObservableProperty]
    public partial int TemperatureC { get; set; }

    [ObservableProperty]
    public partial int SpeedPercent { get; set; }

    public FanCurvePoint ToModel() => new(TemperatureC, SpeedPercent);
}
