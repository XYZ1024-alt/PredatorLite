using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.ViewModels;

namespace PredatorLite.App.Views;

public sealed partial class MainShell : UserControl
{
    private readonly Page[] _pages;

    public MainShell(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _pages =
        [
            new HomePage(viewModel),
            new LightingPage(viewModel),
            new MonitorPage(viewModel),
            new SettingsPage(viewModel)
        ];
        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    public FrameworkElement TitleBarDragRegion => AppTitleBar;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        int selected = Math.Clamp(ViewModel.SelectedTabIndex, 0, _pages.Length - 1);
        Navigation.SelectedItem = Navigation.MenuItems[selected];
        Navigate(selected);
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        int index = tag switch
        {
            "lighting" => 1,
            "monitor" => 2,
            "settings" => 3,
            _ => 0
        };
        Navigate(index);
    }

    private void Navigate(int index)
    {
        ViewModel.SelectedTabIndex = index;
        ContentFrame.Content = _pages[index];
    }
}
