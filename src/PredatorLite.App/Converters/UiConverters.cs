using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PredatorLite.App.Converters;

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is not null && parameter is not null &&
        string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        DependencyProperty.UnsetValue;
}

public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language)
    {
        try
        {
            string hex = value?.ToString()?.TrimStart('#') ?? "00A8E8";
            return new SolidColorBrush(Color.FromArgb(
                255,
                byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }
        catch
        {
            return new SolidColorBrush(Color.FromArgb(255, 0, 168, 232));
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        DependencyProperty.UnsetValue;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language) => value is not true;
}

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        value is Visibility.Visible;
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, string language) =>
        value is Visibility.Collapsed;
}
