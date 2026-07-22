using System.Globalization;
using Microsoft.UI.Xaml;

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
        string resourceName = normalized == "en-US" ? "enUS" : "zhCN";
        ResourceDictionary dictionary = new()
        {
            Source = new Uri($"ms-appx:///Resources/Strings.{resourceName}.xaml")
        };

        IList<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;
        ResourceDictionary? existing = dictionaries.FirstOrDefault(item =>
            item.Source?.OriginalString.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        int position = existing is null ? Math.Min(1, dictionaries.Count) : dictionaries.IndexOf(existing);
        if (existing is not null)
        {
            dictionaries.Remove(existing);
        }

        dictionaries.Insert(position, dictionary);
        CurrentLanguage = normalized;
        CultureInfo culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) ? value?.ToString() ?? key : key;
}
