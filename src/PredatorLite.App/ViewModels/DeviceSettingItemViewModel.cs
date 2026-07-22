using CommunityToolkit.Mvvm.ComponentModel;
using PredatorLite.Core.Models;

namespace PredatorLite.App.ViewModels;

public partial class DeviceSettingItemViewModel : ObservableObject
{
    public DeviceSettingItemViewModel(DeviceSettingState state, string name, string detail)
    {
        Id = state.Id;
        Name = name;
        IsSupported = state.IsSupported;
        IsWritable = state.IsWritable;
        enabled = state.Enabled == true;
        Detail = detail;
    }

    public DeviceSettingId Id { get; }

    public string Name { get; }

    public bool IsSupported { get; }

    public bool IsWritable { get; }

    public string Detail { get; }

    [ObservableProperty]
    private bool enabled;
}
