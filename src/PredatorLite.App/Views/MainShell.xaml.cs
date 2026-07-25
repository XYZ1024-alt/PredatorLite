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
    private readonly Dictionary<AppSection, Func<Page>> _pageFactories;
    private readonly Dictionary<AppSection, Page> _pages = [];
    private readonly UiMotionService _motion;
    private readonly IAppLogger _logger;
    private readonly Action _minimizeWindow;
    private readonly Action _hideWindow;
    private bool _deferHomeContent;
    private AppSection? _currentSection;
    private bool _loaded;

    public MainShell(
        MainViewModel viewModel,
        UiMotionService motion,
        IAppLogger logger,
        Action minimizeWindow,
        Action hideWindow,
        bool deferHomeContent)
    {
        ViewModel = viewModel;
        _motion = motion;
        _logger = logger;
        _minimizeWindow = minimizeWindow;
        _hideWindow = hideWindow;
        _deferHomeContent = deferHomeContent;
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
            [AppSection.Home] = CreateHomePage,
            [AppSection.Cooling] = () => new CoolingPage(viewModel),
            [AppSection.Lighting] = () => new LightingPage(viewModel),
            [AppSection.Monitor] = () => new MonitorPage(viewModel),
            [AppSection.Settings] = () => new SettingsPage(viewModel)
        };
        _motion.AttachPressFeedback(RootLayout);
        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    public FrameworkElement TitleBarDragRegion => TitleBarDragSurface;

    public FrameworkElement CaptionButtonInputRegion => CaptionButtonRegion;

    internal void LoadDeferredHomeContent()
    {
        _deferHomeContent = false;
        if (_pages.TryGetValue(AppSection.Home, out Page? page) && page is HomePage homePage)
        {
            homePage.LoadDeferredContent();
        }
    }

    public void ResetCaptionButtonVisualStates()
    {
        VisualStateManager.GoToState(MinimizeWindowButton, "Normal", useTransitions: false);
        VisualStateManager.GoToState(CloseWindowButton, "Normal", useTransitions: false);
    }

    public bool TryNavigateBack()
    {
        if (_currentSection is null or AppSection.Home)
        {
            return false;
        }

        Navigate(AppSection.Home, animate: true);
        return true;
    }

    private HomePage CreateHomePage()
    {
        HomePage page = new(ViewModel);
        if (!_deferHomeContent)
        {
            page.LoadDeferredContent();
        }

        return page;
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
            _logger.LogError($"Navigation to {section} failed", exception);
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

    private void MinimizeWindowButton_Click(object sender, RoutedEventArgs e) =>
        _minimizeWindow();

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) =>
        _hideWindow();

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
