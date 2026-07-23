using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using Windows.System;

namespace PredatorLite.App.Views;

public sealed partial class MainShell : UserControl
{
    private readonly IReadOnlyDictionary<AppSection, Page> _pages;
    private readonly UiMotionService _motion;
    private AppSection? _currentSection;
    private bool _loaded;

    public MainShell(MainViewModel viewModel, UiMotionService motion)
    {
        ViewModel = viewModel;
        _motion = motion;
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Main shell XAML initialization failed.", exception);
        }

        RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), true);
        _pages = new Dictionary<AppSection, Page>
        {
            [AppSection.Home] = CreatePage(() => new HomePage(viewModel), AppSection.Home),
            [AppSection.Cooling] = CreatePage(() => new CoolingPage(viewModel), AppSection.Cooling),
            [AppSection.Lighting] = CreatePage(() => new LightingPage(viewModel), AppSection.Lighting),
            [AppSection.Monitor] = CreatePage(() => new MonitorPage(viewModel), AppSection.Monitor),
            [AppSection.Settings] = CreatePage(() => new SettingsPage(viewModel), AppSection.Settings)
        };
        _motion.AttachPressFeedback(RootLayout);
        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    public FrameworkElement TitleBarDragRegion => AppTitleBar;

    public bool TryNavigateBack()
    {
        if (_currentSection is null or AppSection.Home)
        {
            return false;
        }

        _ = NavigateAsync(AppSection.Home, animate: true);
        return true;
    }

    private static TPage CreatePage<TPage>(Func<TPage> factory, AppSection section)
        where TPage : Page
    {
        try
        {
            return factory();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"{section} page initialization failed.", exception);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _ = NavigateAsync(ViewModel.SelectedSection, animate: false);
    }

    private async Task NavigateAsync(AppSection section, bool animate)
    {
        if (!_pages.TryGetValue(section, out Page? page))
        {
            section = AppSection.Home;
            page = _pages[section];
        }

        if (_currentSection == section && ContentFrame.Content is not null)
        {
            _motion.Reset(ContentFrame);
            return;
        }

        int direction = section == AppSection.Home ? -1 : 1;
        _currentSection = section;
        ViewModel.SelectedSection = section;
        ContentFrame.Content = page;
        bool isHome = section == AppSection.Home;
        PageTitleText.Text = isHome ? string.Empty : ResolveTitle(section);
        PageTitleText.Visibility = isHome ? Visibility.Collapsed : Visibility.Visible;
        DeviceModelText.Visibility = isHome ? Visibility.Visible : Visibility.Collapsed;
        await _motion.AnimatePageInAsync(ContentFrame, direction, animate);
    }

    private static string ResolveTitle(AppSection section)
    {
        string resourceKey = section switch
        {
            AppSection.Cooling => "Page.Cooling",
            AppSection.Lighting => "Page.Lighting",
            AppSection.Monitor => "Page.Monitor",
            AppSection.Settings => "Page.Settings",
            _ => "App.Name"
        };
        return Application.Current.Resources[resourceKey]?.ToString() ?? "PredatorLite";
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Left && e.KeyStatus.IsMenuKeyDown)
        {
            e.Handled = TryNavigateBack();
        }
    }

    private void Navigation_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        if (sender is RadioButton { Tag: string tag } &&
            Enum.TryParse(tag, ignoreCase: true, out AppSection section))
        {
            _ = NavigateAsync(section, animate: true);
        }
    }
}
