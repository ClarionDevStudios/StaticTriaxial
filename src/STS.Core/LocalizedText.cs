namespace STS.Core;

/// <summary>
/// 一条**可翻译**的消息：资源键 + 格式化参数。译文在界面层的语言字典里。
///
/// 参数里可以再嵌一条 <see cref="LocalizedText"/>（例：「{0}不能为空」的 {0} 是字段名 ✓）——
/// 界面递归解析，于是"字段名"和"句式"各自跟着语言走，方法层一行中文都不用写死。
/// </summary>
public sealed record LocalizedText(string ResourceKey, params object[] Args);

/// <summary>
/// 带资源键的异常：<see cref="Exception.Message"/> 里放**中文兜底**（写日志用），
/// 界面优先按 <see cref="Messages"/> 的资源键取当前语言（用户 2026-10-15："语言切换未处理的内容很多"）。
///
/// 与"参数声明里只放键、中文当兜底"是同款做法：**方法/存储层不认识语言，只带键上路**。
/// 界面对付它的入口是 <c>STS.Ui.Services.UiText.Describe</c>。
/// </summary>
public sealed class LocalizedException(string fallbackMessage, params LocalizedText[] messages)
    : Exception(fallbackMessage)
{
    /// <summary>要显示给用户的那几条消息（按顺序）。</summary>
    public IReadOnlyList<LocalizedText> Messages { get; } = messages;

    /// <summary>一条消息的便捷构造（多数场景只有一条）。</summary>
    public static LocalizedException Single(
        string resourceKey,
        string fallbackMessage,
        params object[] args
    ) => new(fallbackMessage, new LocalizedText(resourceKey, args));
}
