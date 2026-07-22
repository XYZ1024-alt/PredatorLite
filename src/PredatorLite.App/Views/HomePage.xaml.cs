using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.ViewModels;
using PredatorLite.Core.Models;

namespace PredatorLite.App.Views;

public sealed partial class HomePage : Page
{
    private bool _loaded;
    private bool _handlingChargeLimit;

    public HomePage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += (_, _) => _loaded = true;
    }

    public MainViewModel ViewModel { get; }

    public OperatingMode SilentMode => OperatingMode.Silent;
    public OperatingMode BalancedMode => OperatingMode.Balanced;
    public OperatingMode PerformanceMode => OperatingMode.Performance;
    public OperatingMode TurboMode => OperatingMode.Turbo;
    public OperatingMode EcoMode => OperatingMode.Eco;
    public FanMode AutoFanMode => FanMode.Auto;
    public FanMode MaxFanMode => FanMode.Max;
    public GpuMuxMode HybridMuxMode => GpuMuxMode.Hybrid;
    public GpuMuxMode DiscreteMuxMode => GpuMuxMode.Discrete;

    private async void ChargeLimitToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _handlingChargeLimit || sender is not ToggleSwitch toggle)
        {
            return;
        }

        _handlingChargeLimit = true;
        toggle.IsEnabled = false;
        try
        {
            await ViewModel.SetChargeLimitCommand.ExecuteAsync(toggle.IsOn);
        }
        finally
        {
            toggle.IsEnabled = ViewModel.BatteryControlAvailable;
            _handlingChargeLimit = false;
        }
    }
}
