namespace STS.Ui.Services;

using System.Globalization;
using STS.Core;
using STS.Core.Control;

/// <summary>
/// 界面把"异常 / 可翻译消息"渲染成当前语言。
///
/// 规则：<see cref="LocalizedException"/> 优先按资源键取译文（参数递归解析，字段名也跟语言走）；
/// 其余异常退回 <c>Message</c>（那是中文兜底文本，至少不是空白）。所有显示 <c>ex.Message</c> 的地方都该走这里。
/// </summary>
public static class UiText
{
    /// <summary>取一条异常该显示的文字（当前语言）。</summary>
    public static string Describe(ILocalizationService localization, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(exception);

        return Find(exception) is { Messages.Count: > 0 } localized
            ? string.Join(
                localization.GetString("Common.ListSeparator"),
                localized.Messages.Select(message => Format(localization, message))
            )
            : exception.Message;
    }

    /// <summary>沿 <see cref="Exception.InnerException"/> 找带键的那一层（例：ArgumentException 包着它）。</summary>
    private static LocalizedException? Find(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is LocalizedException localized)
            {
                return localized;
            }
        }

        return null;
    }

    /// <summary>渲染一条可翻译消息（参数里嵌套的 <see cref="LocalizedText"/> 也一并解析）。</summary>
    public static string Format(ILocalizationService localization, LocalizedText text)
    {
        var template = localization.GetString(text.ResourceKey);
        if (text.Args.Length == 0)
        {
            return template;
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            template,
            [.. text.Args.Select(arg => arg is LocalizedText nested ? Format(localization, nested) : arg)]
        );
    }

    /// <summary>链路状态的描述：带键的走键（当前语言），否则回落到中文兜底。</summary>
    public static string Describe(ILocalizationService localization, NetworkLinkState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.DetailText is { } text ? Format(localization, text) : state.Detail;
    }
}
