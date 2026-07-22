using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.ViewModels;

namespace PredatorLite.App.Views;

public sealed partial class MonitorPage : Page
{
    public MonitorPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
