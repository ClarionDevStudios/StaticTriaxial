namespace ANX.Ui.Services;

using System.Globalization;

/// <inheritdoc />
public sealed class LocalizationService : ILocalizationService
{
    private const string DictionaryUriFormat =
        "pack://application:,,,/ANX.Ui;component/Resources/Languages/{0}.xaml";

    private ResourceDictionary? _currentDictionary;

    public string CurrentCulture { get; private set; } = "zh-CN";

    public IReadOnlyList<LanguageInfo> Languages { get; } =
    [
        new("zh-CN", "简体中文"),
        new("en-US", "English"),
    ];

    public event EventHandler<string>? LanguageChanged;

    public void SetLanguage(string cultureName)
    {
        var normalized = NormalizeCulture(cultureName);

        if (Application.Current is null)
        {
            CurrentCulture = normalized;
            return;
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;

        if (_currentDictionary is not null)
        {
            dictionaries.Remove(_currentDictionary);
            _currentDictionary = null;
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(string.Format(DictionaryUriFormat, normalized), UriKind.Absolute),
        };

        dictionaries.Add(dictionary);
        _currentDictionary = dictionary;
        CurrentCulture = normalized;

        LanguageChanged?.Invoke(this, normalized);
    }

    public string GetString(string key)
    {
        if (_currentDictionary is not null && _currentDictionary[key] is string value)
        {
            return value;
        }

        return key;
    }

    private static string NormalizeCulture(string cultureName)
    {
        try
        {
            return new CultureInfo(cultureName).Name;
        }
        catch (CultureNotFoundException)
        {
            return "zh-CN";
        }
    }
}
