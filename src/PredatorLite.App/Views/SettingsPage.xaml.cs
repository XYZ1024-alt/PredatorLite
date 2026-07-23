using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.ViewModels;

namespace PredatorLite.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loaded;
    private bool _handlingRunAtStartup;
    private bool _handlingFps;
    private bool _savingPreferences;
    private bool _preferencesDirty;

    public SettingsPage(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += (_, _) => _loaded = true;
    }

    public MainViewModel ViewModel { get; }

    private async void RunAtStartup_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _handlingRunAtStartup || sender is not ToggleSwitch toggle)
        {
            return;
        }

        _handlingRunAtStartup = true;
        toggle.IsEnabled = false;
        try
        {
            await ViewModel.SetRunAtStartupCommand.ExecuteAsync(toggle.IsOn);
        }
        finally
        {
            toggle.IsEnabled = true;
            _handlingRunAtStartup = false;
        }
    }

    private async void Preference_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        if (_savingPreferences)
        {
            _preferencesDirty = true;
            return;
        }

        _savingPreferences = true;
        try
        {
            do
            {
                _preferencesDirty = false;
                await ViewModel.SavePreferencesCommand.ExecuteAsync(null);
            }
            while (_preferencesDirty);
        }
        finally
        {
            _savingPreferences = false;
        }
    }

    private async void ShowFps_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _handlingFps || sender is not ToggleSwitch toggle)
        {
            return;
        }

        _handlingFps = true;
        toggle.IsEnabled = false;
        try
        {
            await ViewModel.SetFpsEnabledCommand.ExecuteAsync(toggle.IsOn);
        }
        finally
        {
            toggle.IsEnabled = true;
            _handlingFps = false;
        }
    }

}
