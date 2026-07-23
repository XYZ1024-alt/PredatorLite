using CommunityToolkit.Mvvm.ComponentModel;
using PredatorLite.Core.Models;
using PredatorLite.Core.Services;

namespace PredatorLite.App.ViewModels;

public partial class FanCurvePointViewModel : ObservableObject
{
    public FanCurvePointViewModel(
        FanCurvePoint point,
        FanCurveChannel channel,
        int index,
        bool isSafetyEndpoint)
    {
        Channel = channel;
        Index = index;
        IsSafetyEndpoint = isSafetyEndpoint;
        TemperatureC = isSafetyEndpoint ? FanCurveEngine.SafetyTemperatureC : point.TemperatureC;
        SpeedPercent = isSafetyEndpoint ? FanCurveEngine.SafetySpeedPercent : point.SpeedPercent;
    }

    public FanCurveChannel Channel { get; }

    public int Index { get; }

    public int DisplayIndex => Index + 1;

    public bool IsSafetyEndpoint { get; }

    public bool IsEditable => !IsSafetyEndpoint;

    public string TemperatureAutomationId => $"FanCurve.{Channel}.{Index}.Temperature";

    public string SpeedAutomationId => $"FanCurve.{Channel}.{Index}.Speed";

    [ObservableProperty]
    public partial int TemperatureC { get; set; }

    [ObservableProperty]
    public partial int SpeedPercent { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasValidationIssue { get; set; }

    public void SetValidation(string message)
    {
        ValidationMessage = message;
        HasValidationIssue = !string.IsNullOrWhiteSpace(message);
    }

    public FanCurvePoint ToModel() => new(TemperatureC, SpeedPercent);
}
