using System.Globalization;
using Microsoft.UI.Xaml;

namespace PredatorLite.App.Services;

public sealed class LocalizationService
{
    private ResourceDictionary _strings = new();
    private int _resourcePosition = -1;

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage { get; private set; } = "zh-CN";

    public void SetLanguage(string language)
    {
        string normalized = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";
        if (_strings.Count > 0 && string.Equals(normalized, CurrentLanguage, StringComparison.Ordinal))
        {
            return;
        }

        string resourceName = normalized == "en-US" ? "enUS" : "zhCN";
        ResourceDictionary strings = new()
        {
            Source = new Uri($"ms-appx:///Resources/Strings.{resourceName}.xaml")
        };

        IList<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_resourcePosition >= 0 && _resourcePosition < dictionaries.Count)
        {
            dictionaries.RemoveAt(_resourcePosition);
        }

        _resourcePosition = Math.Min(1, dictionaries.Count);
        dictionaries.Insert(_resourcePosition, strings);
        _strings = strings;
        CurrentLanguage = normalized;
        CultureInfo culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        try
        {
            return _strings[key] is string text ? text : key;
        }
        catch (Exception exception) when (
            exception is KeyNotFoundException or System.Runtime.InteropServices.ExternalException)
        {
            return key;
        }
    }
}
