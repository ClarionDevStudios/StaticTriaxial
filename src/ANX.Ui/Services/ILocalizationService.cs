namespace ANX.Ui.Services;

/// <summary>一门受支持的界面语言。</summary>
public sealed record LanguageInfo(string Code, string DisplayName);

/// <summary>
/// 界面多语言。语言列表由资源文件驱动，不写死按钮（现役就是两个写死的按钮，加第三门得改代码）。
/// </summary>
public interface ILocalizationService
{
    /// <summary>当前语言代码（例 zh-CN）。</summary>
    string CurrentCulture { get; }

    /// <summary>受支持的语言列表。</summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>语言切换完成。</summary>
    event EventHandler<string>? LanguageChanged;

    /// <summary>取资源键对应的文案；取不到返回键名本身（会由资源键检查工具兜住）。</summary>
    string GetString(string key);

    /// <summary>切换语言。</summary>
    void SetLanguage(string cultureName);
}
