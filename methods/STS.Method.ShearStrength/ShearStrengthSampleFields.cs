namespace STS.Method.ShearStrength;

using System.Globalization;
using STS.Control;

/// <summary>
/// T 1236 的**试样信息**字段声明（逐件填录、随试件存进明细行；**不是**方法级参数）。
///
/// 为什么要逐件：筛件的初始尺寸决定每件的判据与加载速率（h0/d0 逐件不同），围压 σ3 是
/// "每件只做一级"（规范 4.8），质量 / 含水率是 4.7 试验后称测、报告 6.7 要用的数据。
/// 字段键 = 明细表列名（`ShearStrengthPersistenceModule.DetailTable` 已声明，写库直接落列）。
/// </summary>
public static class ShearStrengthSampleFields
{
    /// <summary>试验围压 σ3（kPa）。</summary>
    public const string ConfiningPressureKey = "ConfiningPressure";

    /// <summary>试件初始高度 h0（mm）。</summary>
    public const string InitialHeightKey = "InitialHeight";

    /// <summary>试件初始直径 d0（mm）。</summary>
    public const string InitialDiameterKey = "InitialDiameter";

    /// <summary>试件总质量（g，试验后称量）。</summary>
    public const string MassKey = "Mass";

    /// <summary>含水率 w（%，按 T 1206 测定）。</summary>
    public const string WaterContentKey = "WaterContent";

    /// <summary>干密度（g/cm³）。</summary>
    public const string DryDensityKey = "DryDensity";

    /// <summary>含水率损失（%）。</summary>
    public const string WaterContentLossKey = "WaterContentLoss";

    /// <summary>试样描述（自由文本，可空）。</summary>
    public const string SampleDescKey = "SampleDesc";

    /// <summary>
    /// 字段声明（试样编辑页按它渲染）。次序：试验条件（围压）→ 初始尺寸（判据/速率要用）→
    /// 试验后数据（可空）→ 描述。尺寸默认值取标准件 Φ100 × 306（规范 4.1.3 的合格窗中值）。
    /// </summary>
    public static readonly IReadOnlyList<ParameterDefinition> Definitions =
        [.. RawDefinitions().Select(definition => definition.WithTextKeys())];

    /// <summary>中文兜底的原始声明（键由 <see cref="ParameterDefinition.WithTextKeys"/> 按约定补）。</summary>
    private static IReadOnlyList<ParameterDefinition> RawDefinitions() =>
    [
        new(ConfiningPressureKey, "试验围压 σ3", "kPa", "30",
            Kind: ParameterKind.Number, Minimum: 1, Maximum: 1000,
            Scope: ParameterScope.Computation,
            Description: "该试件试验时施加的围压（kPa）。规范 4.8：围压不少于 3 级（本方法按 30 / 60 / 100），"
                + "每级 3 件，每件只做一级。"),
        new(InitialHeightKey, "试件初始高度 h0", "mm", "306",
            Kind: ParameterKind.Number, Minimum: 100, Maximum: 500,
            Scope: ParameterScope.Computation,
            Description: "规范 4.1.3：转动 120° 测 3 次取平均值；合格窗 306mm±5mm（标准件）。"),
        new(InitialDiameterKey, "试件初始直径 d0", "mm", "100",
            Kind: ParameterKind.Number, Minimum: 10, Maximum: 200,
            Scope: ParameterScope.Computation,
            Description: "规范 4.1.3：上 / 中 / 下各测 3 次取平均值；标准件 Φ100mm。"),
        new(MassKey, "试件总质量", "g", "",
            Kind: ParameterKind.Number, Minimum: 0, Maximum: 100000,
            Scope: ParameterScope.Computation, AllowEmpty: true, PostTest: true,
            Description: "规范 4.7：试验结束后称量的试件总质量（报告 6.7 用）。"),
        new(WaterContentKey, "含水率 w", "%", "",
            Kind: ParameterKind.Number, Minimum: 0, Maximum: 100,
            Scope: ParameterScope.Computation, AllowEmpty: true, PostTest: true,
            Description: "规范 4.7：按本规程 T 1206 测定，精确至 0.1%（报告 6.7）。"),
        new(DryDensityKey, "干密度", "g/cm³", "",
            Kind: ParameterKind.Number, Minimum: 0, Maximum: 10,
            Scope: ParameterScope.Computation, AllowEmpty: true, PostTest: true,
            Description: "报告 6.7 指标（录入；可留空）。"),
        new(WaterContentLossKey, "含水率损失", "%", "",
            Kind: ParameterKind.Number, Minimum: 0, Maximum: 100,
            Scope: ParameterScope.Computation, AllowEmpty: true, PostTest: true,
            Description: "报告 6.7 指标（录入；可留空）。"),
        new(SampleDescKey, "试样描述", "", "",
            Kind: ParameterKind.Text, Scope: ParameterScope.Computation, AllowEmpty: true,
            Description: "自由文本（可留空）。"),
    ];

    /// <summary>字段值 → 数值（键不存在或解析失败回落默认值；默认值为空时回 0）。</summary>
    public static double GetDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        var definition = Definitions.First(definition =>
            string.Equals(definition.Key, key, StringComparison.Ordinal)
        );

        return values.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.TryParse(
                    definition.DefaultValue,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var fallback
                )
                    ? fallback
                    : 0;
    }

    /// <summary>按键取该字段的**显示名资源键**（报错信息里嵌字段名用，界面按当前语言展开）。</summary>
    public static string NameKey(string key) =>
        Definitions
            .First(definition => string.Equals(definition.Key, key, StringComparison.Ordinal))
            .DisplayNameKey;
}
