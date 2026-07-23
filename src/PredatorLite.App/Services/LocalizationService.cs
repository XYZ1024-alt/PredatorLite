using System.Globalization;
using System.Xml.Linq;
using Microsoft.UI.Xaml;

namespace PredatorLite.App.Services;

public sealed class LocalizationService
{
    private IReadOnlyDictionary<string, string> _strings = new Dictionary<string, string>();
    private int _resourcePosition = -1;

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage { get; private set; } = "zh-CN";

    public void SetLanguage(string language)
    {
        string normalized = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en-US"
            : "zh-CN";
        string resourceName = normalized == "en-US" ? "enUS" : "zhCN";
        IReadOnlyDictionary<string, string> strings = LoadStrings(resourceName);
        ResourceDictionary dictionary = new();
        foreach ((string key, string value) in strings)
        {
            dictionary.Add(key, value);
        }

        IList<ResourceDictionary> dictionaries = Application.Current.Resources.MergedDictionaries;
        if (_resourcePosition >= 0 && _resourcePosition < dictionaries.Count)
        {
            dictionaries.RemoveAt(_resourcePosition);
        }

        _resourcePosition = Math.Min(1, dictionaries.Count);
        dictionaries.Insert(_resourcePosition, dictionary);
        _strings = strings;
        CurrentLanguage = normalized;
        CultureInfo culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        return _strings.TryGetValue(key, out string? value) ? value : key;
    }

    private static IReadOnlyDictionary<string, string> LoadStrings(string resourceName)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Localization",
            $"Strings.{resourceName}.xaml");
        XDocument document = XDocument.Load(path, LoadOptions.None);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "String")
            .Select(element => new
            {
                Key = element.Attribute(xaml + "Key")?.Value,
                Value = element.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(
                item => item.Key!,
                item => item.Value,
                StringComparer.Ordinal);
    }
}
