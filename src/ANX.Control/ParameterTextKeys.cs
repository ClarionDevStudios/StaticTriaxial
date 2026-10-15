namespace ANX.Control;

/// <summary>
/// 参数 / 试样信息字段的**显示文本资源键**（用户 2026-10-15：语言切换要把这些也盖住）。
///
/// 约定：显示名 <c>Field.&lt;参数键&gt;</c>、说明 <c>Field.&lt;参数键&gt;.Desc</c>、下拉选项
/// <c>Field.&lt;参数键&gt;.&lt;选项值&gt;</c>。文本本身在界面层的语言字典里（与
/// <c>MetricDefinition.DisplayNameKey</c> 同款做法：声明只放键）。
///
/// 为什么用"参数键"而不是给每项再起一个名字：参数键（<c>LoadRate</c> / <c>SampleWidth</c>…）
/// 本身就是稳定的、跨方法复用的（"后续方法的字段名必须与现役软件一致"），拿它当资源键的第二段
/// 既省事又不会漂。**声明里给了键就必须在两种语言里都有译文** —— 由
/// <c>ParameterLocalizationTests</c> 对着语言字典把守。
/// </summary>
public static class ParameterTextKeys
{
    /// <summary>显示名。</summary>
    public static string Name(string parameterKey) => $"Field.{parameterKey}";

    /// <summary>说明（界面上显示在名称下方的那句话）。</summary>
    public static string Description(string parameterKey) => $"Field.{parameterKey}.Desc";

    /// <summary>下拉选项。</summary>
    public static string Option(string parameterKey, string optionValue) =>
        $"Field.{parameterKey}.{optionValue}";
}
