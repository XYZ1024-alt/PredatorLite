using CommunityToolkit.Mvvm.ComponentModel;
using PredatorLite.Core.Models;

namespace PredatorLite.App.ViewModels;

public partial class FanCurvePointViewModel : ObservableObject
{
    public FanCurvePointViewModel(FanCurvePoint point)
    {
        temperatureC = point.TemperatureC;
        speedPercent = point.SpeedPercent;
    }

    [ObservableProperty]
    private int temperatureC;

    [ObservableProperty]
    private int speedPercent;

    public FanCurvePoint ToModel() => new(TemperatureC, SpeedPercent);
}
