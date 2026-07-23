using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.ViewModels;

namespace PredatorLite.App.Views;

public sealed partial class OsdContent : UserControl, IDisposable
{
    private bool _disposed;

    public OsdContent(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModelOnPropertyChanged;
        UpdateFpsColumn();
    }

    public MainViewModel ViewModel { get; }

    public void RefreshLocalizedContent() => UpdateFpsColumn();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ViewModel.PropertyChanged -= ViewModelOnPropertyChanged;
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ShowFps))
        {
            UpdateFpsColumn();
        }
    }

    private void UpdateFpsColumn()
    {
        FpsColumn.Width = ViewModel.ShowFps
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
    }
}
