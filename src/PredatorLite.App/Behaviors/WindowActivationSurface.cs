using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PredatorLite.App.Behaviors;

public static class WindowActivationSurface
{
    private static readonly List<WeakReference<Border>> Surfaces = [];
    private static bool _isWindowActive = true;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WindowActivationSurface),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ActiveBackgroundProperty = DependencyProperty.RegisterAttached(
        "ActiveBackground",
        typeof(Brush),
        typeof(WindowActivationSurface),
        new PropertyMetadata(null, OnBackgroundChanged));

    public static readonly DependencyProperty InactiveBackgroundProperty = DependencyProperty.RegisterAttached(
        "InactiveBackground",
        typeof(Brush),
        typeof(WindowActivationSurface),
        new PropertyMetadata(null, OnBackgroundChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static Brush? GetActiveBackground(DependencyObject element) =>
        (Brush?)element.GetValue(ActiveBackgroundProperty);

    public static void SetActiveBackground(DependencyObject element, Brush? value) =>
        element.SetValue(ActiveBackgroundProperty, value);

    public static Brush? GetInactiveBackground(DependencyObject element) =>
        (Brush?)element.GetValue(InactiveBackgroundProperty);

    public static void SetInactiveBackground(DependencyObject element, Brush? value) =>
        element.SetValue(InactiveBackgroundProperty, value);

    internal static void SetWindowActive(bool isWindowActive)
    {
        if (_isWindowActive == isWindowActive)
        {
            return;
        }

        _isWindowActive = isWindowActive;
        for (int index = Surfaces.Count - 1; index >= 0; index--)
        {
            if (Surfaces[index].TryGetTarget(out Border? surface))
            {
                UpdateBackground(surface);
            }
            else
            {
                Surfaces.RemoveAt(index);
            }
        }
    }

    private static void OnIsEnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is not Border surface)
        {
            return;
        }

        bool isEnabled = (bool)args.NewValue;
        if (isEnabled)
        {
            RemoveSurface(surface);
            Surfaces.Add(new WeakReference<Border>(surface));
            surface.ActualThemeChanged += OnActualThemeChanged;
            UpdateBackground(surface);
        }
        else
        {
            surface.ActualThemeChanged -= OnActualThemeChanged;
            RemoveSurface(surface);
        }
    }

    private static void OnBackgroundChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is Border surface && GetIsEnabled(surface))
        {
            UpdateBackground(surface);
        }
    }

    private static void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (sender is Border surface)
        {
            UpdateBackground(surface);
        }
    }

    private static void UpdateBackground(Border surface)
    {
        Brush? background = _isWindowActive
            ? GetActiveBackground(surface)
            : GetInactiveBackground(surface);
        if (background is not null)
        {
            surface.Background = background;
        }
    }

    private static void RemoveSurface(Border surface)
    {
        for (int index = Surfaces.Count - 1; index >= 0; index--)
        {
            if (!Surfaces[index].TryGetTarget(out Border? candidate) ||
                ReferenceEquals(candidate, surface))
            {
                Surfaces.RemoveAt(index);
            }
        }
    }
}
