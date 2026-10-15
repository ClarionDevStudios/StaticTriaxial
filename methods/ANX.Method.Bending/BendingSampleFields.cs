namespace ANX.Method.Bending;

using System.Globalization;
using ANX.Control;

/// <summary>
/// T 0715 的**试样信息**字段声明（逐件填录、随试件存进明细行；**不是**方法级参数）。
/// 弯曲强度 / 最大弯拉应变要用它们，所以必须跟着"这一件"走，而不是全方法共用一份。
/// </summary>
public static class BendingSampleFields
{
    /// <summary>跨径 L（mm）。</summary>
    public const string SpanLengthKey = "SpanLength";

    /// <summary>
    /// 试件长度（mm）。**只进报表、不参与判定** —— 弯曲强度与最大弯拉应变只吃跨径与 b/h；
    /// 但现役的试样信息里有这一格、报表明细也有这一列，所以逐件记录。
    /// </summary>
    public const string SampleLengthKey = "SampleLength";

    /// <summary>试件宽度 b（mm）。</summary>
    public const string SampleWidthKey = "SampleWidth";

    /// <summary>试件高度 h（mm）。</summary>
    public const string SampleHeightKey = "SampleHeight";

    /// <summary>
    /// 字段声明（试样编辑页按它渲染，与参数页共用同一套编辑控件）。
    ///
    /// **次序照现役窗体**（`uEditBendingTestSampleForm.dfm` 按控件 Top/Left 还原出来的实际版面）：
    /// 试样长度 → 试样宽度 → 试样高度 → 跨径 —— 其中"**长度排在宽度上面**"是用户 2026-10-15 指名的一条。
    /// （现役窗体里长度/宽度/高度还各有一句注释：宽度与高度是"3 处测量的平均值"、跨径是"支点间距"。）
    /// </summary>
    public static readonly IReadOnlyList<ParameterDefinition> Definitions =
        [.. RawDefinitions().Select(definition => definition.WithTextKeys())];

    /// <summary>中文兜底的原始声明（键由 <see cref="ParameterDefinition.WithTextKeys"/> 按约定补）。</summary>
    private static IReadOnlyList<ParameterDefinition> RawDefinitions() =>
    [
        new(SpanLengthKey, "跨径 L", "mm", "200",
            Kind: ParameterKind.Number, Minimum: 10, Maximum: 1000, Scope: ParameterScope.Computation),
        // 默认 250 照现役（`FDefaultLength := 250`，三种公称粒径档都是 250；规范 T 0715 也写 250±2mm）
        new(SampleLengthKey, "试件长度", "mm", "250",
            Kind: ParameterKind.Number, Minimum: 10, Maximum: 1000, Scope: ParameterScope.Computation),
        new(SampleWidthKey, "试件宽度 b", "mm", "30",
            Kind: ParameterKind.Number, Minimum: 1, Maximum: 500, Scope: ParameterScope.Computation),
        new(SampleHeightKey, "试件高度 h", "mm", "35",
            Kind: ParameterKind.Number, Minimum: 1, Maximum: 500, Scope: ParameterScope.Computation),
    ];

    /// <summary>字段值 → 工程量（解析失败回落默认值）。</summary>
    public static double GetDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        var definition = Definitions.First(d => string.Equals(d.Key, key, StringComparison.Ordinal));

        return values.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.Parse(definition.DefaultValue, CultureInfo.InvariantCulture);
    }
}
