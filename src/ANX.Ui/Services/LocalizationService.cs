namespace ANX.Ui.Services;

using System.Globalization;

/// <inheritdoc />
public sealed class LocalizationService : ILocalizationService
{
    private const string DictionaryUriFormat =
        "pack://application:,,,/ANX.Ui;component/Resources/Languages/{0}.xaml";

    /// <summary>已加载的语言字典（按语言码缓存）—— 报表按语种取译文时不必重复加载。</summary>
    private readonly Dictionary<string, ResourceDictionary> _cache = new(StringComparer.OrdinalIgnoreCase);

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

        var dictionary = ResolveDictionary(normalized);
        if (dictionary is null)
        {
            return;
        }

        dictionaries.Add(dictionary);
        _currentDictionary = dictionary;
        CurrentCulture = normalized;

        LanguageChanged?.Invoke(this, normalized);
    }

    public string GetString(string key) => GetString(key, CurrentCulture);

    public string GetString(string key, string culture)
    {
        var normalized = NormalizeCulture(culture);

        // 当前语言用已经挂到应用资源上的那份；其它语言按需加载并缓存
        var dictionary =
            string.Equals(normalized, CurrentCulture, StringComparison.OrdinalIgnoreCase)
                ? _currentDictionary ?? ResolveDictionary(normalized)
                : ResolveDictionary(normalized);

        return dictionary?[key] is string value ? value : $"⚠{key}";
    }

    /// <summary>按语言码加载字典（带缓存）。没有 WPF 应用时返回 null（无界面场景按缺键处理）。</summary>
    private ResourceDictionary? ResolveDictionary(string culture)
    {
        if (_cache.TryGetValue(culture, out var cached))
        {
            return cached;
        }

        if (Application.Current is null)
        {
            return null;
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri(string.Format(DictionaryUriFormat, culture), UriKind.Absolute),
        };

        _cache[culture] = dictionary;
        return dictionary;
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
