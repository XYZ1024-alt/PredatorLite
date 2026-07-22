using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.EfficiencyMode;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.App.Services;

namespace PredatorLite.App.Views;

public sealed partial class TrayIconView : UserControl, IDisposable
{
    private Action? _showWindow;
    private Action? _exit;

    public TrayIconView()
    {
        InitializeComponent();
    }

    public void Configure(LocalizationService localization, Action showWindow, Action exit)
    {
        _showWindow = showWindow;
        _exit = exit;
        RebuildMenu(localization);
    }

    public void RebuildMenu(LocalizationService localization)
    {
        OpenMenuItem.Text = localization.Get("Action.Open");
        ExitMenuItem.Text = localization.Get("Action.Exit");
    }

    public void ForceCreate() => TrayIcon.ForceCreate(enablesEfficiencyMode: false);

    public void SetWindowVisible(bool visible)
    {
        if (!visible)
        {
            EfficiencyModeUtilities.SetEfficiencyMode(true);
        }
        else
        {
            EfficiencyModeUtilities.SetEfficiencyMode(false);
        }
    }

    public void Dispose() => TrayIcon.Dispose();

    [RelayCommand]
    private void OpenWindow() => _showWindow?.Invoke();

    [RelayCommand]
    private void Exit() => _exit?.Invoke();
}
