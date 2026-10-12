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

    /// <summary>试件宽度 b（mm）。</summary>
    public const string SampleWidthKey = "SampleWidth";

    /// <summary>试件高度 h（mm）。</summary>
    public const string SampleHeightKey = "SampleHeight";

    /// <summary>字段声明（试样编辑页按它渲染，与参数页共用同一套编辑控件）。</summary>
    public static readonly IReadOnlyList<ParameterDefinition> Definitions =
    [
        new(SpanLengthKey, "跨径 L", "mm", "200",
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
