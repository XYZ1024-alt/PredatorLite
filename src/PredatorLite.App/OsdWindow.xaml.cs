using System.Windows;
using PredatorLite.App.ViewModels;

namespace PredatorLite.App;

public partial class OsdWindow : Window
{
    public OsdWindow(MainViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 18;
        Top = workArea.Top + 18;
    }
}
