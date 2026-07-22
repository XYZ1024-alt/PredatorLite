using System.Globalization;
using System.Windows;

namespace PredatorLite.App.Services;

public sealed class LocalizationService
{
    public event EventHandler? LanguageChanged;

    public string CurrentLanguage { get; private set; } = "zh-CN";

    public void SetLanguage(string language)
    {
        string normalized = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";
        ResourceDictionary dictionary = new()
        {
            Source = new Uri($"Resources/Strings.{normalized}.xaml", UriKind.Relative)
        };

        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(item =>
            item.Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        dictionaries.Insert(0, dictionary);
        CurrentLanguage = normalized;
        CultureInfo culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key) =>
        System.Windows.Application.Current.TryFindResource(key)?.ToString() ?? key;
}
