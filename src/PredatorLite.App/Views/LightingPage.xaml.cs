using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.ViewModels;

namespace PredatorLite.App.Views;

public sealed partial class LightingPage : Page
{
    public LightingPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }

    private void ZoneColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: LightingZoneViewModel zone })
        {
            ViewModel.PickZoneColorCommand.Execute(zone);
        }
    }
}
