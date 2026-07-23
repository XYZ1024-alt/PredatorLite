using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.ViewModels;
using PredatorLite.Core.Models;

namespace PredatorLite.App.Views;

public sealed partial class CoolingPage : Page
{
    public CoolingPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        CurveSelector.SelectedItem = ViewModel.SelectedFanCurveChannel == FanCurveChannel.Gpu
            ? GpuCurveSelector
            : CpuCurveSelector;
    }

    public MainViewModel ViewModel { get; }

    public FanMode AutoFanMode => FanMode.Auto;

    public FanMode MaxFanMode => FanMode.Max;

    public FanCurveChannel CpuFanCurveChannel => FanCurveChannel.Cpu;

    public FanCurveChannel GpuFanCurveChannel => FanCurveChannel.Gpu;

    public static Visibility ChannelVisibility(
        FanCurveChannel selectedChannel,
        FanCurveChannel expectedChannel) =>
        selectedChannel == expectedChannel ? Visibility.Visible : Visibility.Collapsed;

    private void CurveSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ViewModel.SelectedFanCurveChannel =
            ReferenceEquals(sender.SelectedItem, GpuCurveSelector)
                ? FanCurveChannel.Gpu
                : FanCurveChannel.Cpu;
    }
}
