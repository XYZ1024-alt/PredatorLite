using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PredatorLite.App.Services;
using PredatorLite.App.ViewModels;
using PredatorLite.Core.Abstractions;
using Windows.System;

namespace PredatorLite.App.Views;

public sealed partial class MainShell : UserControl
{
    private readonly IReadOnlyDictionary<AppSection, Func<Page>> _pageFactories;
    private readonly Dictionary<AppSection, Page> _pages = [];
    private readonly UiMotionService _motion;
    private readonly IAppLogger _logger;
    private AppSection? _currentSection;
    private bool _loaded;

    public MainShell(MainViewModel viewModel, UiMotionService motion, IAppLogger logger)
    {
        ViewModel = viewModel;
        _motion = motion;
        _logger = logger;
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Main shell XAML initialization failed.", exception);
        }

        RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnRootKeyDown), true);
        _pageFactories = new Dictionary<AppSection, Func<Page>>
        {
            [AppSection.Home] = () => new HomePage(viewModel),
            [AppSection.Cooling] = () => new CoolingPage(viewModel),
            [AppSection.Lighting] = () => new LightingPage(viewModel),
            [AppSection.Monitor] = () => new MonitorPage(viewModel),
            [AppSection.Settings] = () => new SettingsPage(viewModel)
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

        Navigate(AppSection.Home, animate: true);
        return true;
    }

    private Page GetOrCreatePage(AppSection section)
    {
        if (_pages.TryGetValue(section, out Page? page))
        {
            return page;
        }

        Func<Page> factory = _pageFactories[section];
        try
        {
            page = factory();
            _pages.Add(section, page);
            _logger.Info($"UI page created: {section}.");
            return page;
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
        Navigate(ViewModel.SelectedSection, animate: false);
    }

    private void Navigate(AppSection section, bool animate) =>
        _ = NavigateSafelyAsync(section, animate);

    private async Task NavigateSafelyAsync(AppSection section, bool animate)
    {
        try
        {
            await NavigateAsync(section, animate);
        }
        catch (Exception exception)
        {
            _logger.Error($"Navigation to {section} failed", exception);
        }
    }

    private async Task NavigateAsync(AppSection section, bool animate)
    {
        if (!_pageFactories.ContainsKey(section))
        {
            section = AppSection.Home;
        }

        Page page = GetOrCreatePage(section);

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
            Navigate(section, animate: true);
        }
    }
}
