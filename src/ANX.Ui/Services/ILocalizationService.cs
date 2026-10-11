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

    /// <summary>取资源键对应的文案；取不到返回 <c>⚠键名</c>（IAP 的口径：缺键要一眼看得出来）。</summary>
    string GetString(string key);

    /// <summary>
    /// 取指定语言的文案。报表要按语种出图（曲线轴名、图题），不能靠"切换界面语言"来实现，
    /// 所以留这个按语种查询的口子。
    /// </summary>
    string GetString(string key, string culture);

    /// <summary>切换语言。</summary>
    void SetLanguage(string cultureName);
}
