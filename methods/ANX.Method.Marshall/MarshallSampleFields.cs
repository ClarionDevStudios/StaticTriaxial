namespace ANX.Method.Marshall;

using System.Globalization;
using ANX.Control;

/// <summary>
/// T 0709 的**试样信息**字段声明（逐件填录、随试件存进明细行；**不是**方法级参数）。
///
/// **键 = 明细表的列名**（框架按列名写库，见 `TestSheetService.UpsertSampleInfoAsync`）。
/// 字段与次序照现役试样信息窗 `uEditMarshallStabilityTestSampleForm.dfm` 的版面（按 Top 值还原）：
/// 试样分组 → **初始直径 → 初始高度** → （试验温度）→ 加载速度。
/// 与现役的**两处有意差异**：
/// <list type="number">
///   <item>不做逐件"试验温度"：它是该试验单的试验条件，留在试验单上（报表与统计用的都是单上的温度）；</item>
///   <item>不做"试样描述"：本项目框架的试样信息目前**只支持数值 / 选项列**（存库走
///         <c>IReadOnlyDictionary&lt;string, double&gt;</c>），要加得先扩框架 —— 待用户定。</item>
/// </list>
/// </summary>
public static class MarshallSampleFields
{
    /// <summary>试样分组：0 = 第一组、1 = 第二组（现役 GroupID；浸水/真空饱水试验的第二组）。</summary>
    public const string GroupIdKey = "GroupID";

    /// <summary>试件初始直径（mm）。规范：φ101.6±0.25（大型 φ152.4）。</summary>
    public const string SampleInitialDiameterKey = "SampleInitialDiameter";

    /// <summary>试件初始高度（mm）。规范：63.5±1.3（大型 95.3±2.5）；芯样马歇尔按它查高度修正系数。</summary>
    public const string SampleInitialHeightKey = "SampleInitialHeight";

    /// <summary>加载速度（mm/min，现役试样信息里的逐件记录；实际控制按参数表的加载速率走）。</summary>
    public const string StrainRateKey = "StrainRate";

    /// <summary>分组选项（与现役下拉框一致：第一组 / 第二组）。</summary>
    public static readonly IReadOnlyList<ParameterOption> GroupOptions =
    [
        new("0", "第一组", ParameterTextKeys.Option(GroupIdKey, "0")),
        new("1", "第二组", ParameterTextKeys.Option(GroupIdKey, "1")),
    ];

    /// <summary>字段声明（试样编辑页按它渲染，与参数页共用同一套编辑控件）。</summary>
    public static readonly IReadOnlyList<ParameterDefinition> Definitions =
        [.. RawDefinitions().Select(definition => definition.WithTextKeys())];

    /// <summary>中文兜底的原始声明（键由 <see cref="ParameterDefinition.WithTextKeys"/> 按约定补）。</summary>
    private static IReadOnlyList<ParameterDefinition> RawDefinitions() =>
    [
        new(GroupIdKey, "试样分组", "", "0",
            Kind: ParameterKind.Choice, Scope: ParameterScope.Computation, Options: GroupOptions,
            Description: "第一组 = 标准马歇尔试件；浸水马歇尔与真空饱水马歇尔的第二组就是浸水/真空饱水后的那一组。"),
        new(SampleInitialDiameterKey, "初始直径", "mm", "101.6",
            Kind: ParameterKind.Number, Minimum: 50, Maximum: 300, Scope: ParameterScope.Computation,
            Description: "规范：φ101.6±0.25（大型 φ152.4）。芯样马歇尔按实测直径记录。"),
        new(SampleInitialHeightKey, "初始高度", "mm", "63.5",
            Kind: ParameterKind.Number, Minimum: 10, Maximum: 200, Scope: ParameterScope.Computation,
            Description: "规范：63.5±1.3（大型 95.3±2.5）。芯样马歇尔的稳定度按它查高度修正系数 K。"),
        new(StrainRateKey, "加载速度", "mm/min", "50",
            Kind: ParameterKind.Number, Minimum: 0.1, Maximum: 50, Scope: ParameterScope.Computation,
            Description: "随试件存档的记录值（现役试样信息里逐件填）。试验实际按参数表的加载速率运行。"),
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
