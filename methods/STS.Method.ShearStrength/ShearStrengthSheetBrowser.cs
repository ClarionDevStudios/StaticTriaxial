namespace STS.Method.ShearStrength;

using System.Globalization;
using STS.Control;
using STS.Core;
using STS.Core.TestSheets;
using STS.Storage;

/// <summary>
/// 历史数据页 / 试验单弹窗的数据来源：查单、读试件明细、读曲线、读曲线标注。
///
/// 用户 2026-10-17 报的三个问题（历史数据页空、上次试验项目不可用、已有试验单列表为空）根因是同一个：
/// 本类原先是**骨架占位**（四个方法全回空），历史页与试验单弹窗都靠它取数。现在接到
/// <see cref="TestSheetService"/> 上（照 ANX 的 `BendingSheetBrowser` 同款口径）。
/// </summary>
public sealed class ShearStrengthSheetBrowser(TestSheetService sheets) : ITestSheetBrowser
{
    /// <summary>曲线横轴：轴向变形。</summary>
    public string CurveXAxisKey => "Chart.Axis.Deformation";

    /// <summary>曲线纵轴：轴向荷载。</summary>
    public string CurveYAxisKey => "Chart.Axis.Load";

    /// <summary>
    /// 试件列表要读的明细列 + **列名的显示资源键**（界面按当前语言取；用户 2026-10-15：
    /// 不把库里的列名 <c>SampleID</c> 这种直接显示出来）。**第一列必须是试件号** ——
    /// 历史页把第一列当列表标题、其余列做字段表（见 <c>HistoryViewModel.RebuildSamples</c>）。
    /// 有现成指标名的列复用指标键；试样信息列复用字段声明里的显示名键。
    /// </summary>
    private static readonly (string Column, string DisplayKey)[] SampleColumns =
    [
        ("SampleID", ShearStrengthMetricKeys.SampleId),
        ("ConfiningPressure", FieldName(ShearStrengthSampleFields.ConfiningPressureKey)),
        ("InitialHeight", FieldName(ShearStrengthSampleFields.InitialHeightKey)),
        ("InitialDiameter", FieldName(ShearStrengthSampleFields.InitialDiameterKey)),
        ("MaxAxialLoad", ShearStrengthMetricKeys.PeakLoad),
        ("PeakDeformation", ShearStrengthMetricKeys.PeakDeformation),
        ("AxialStrain", ShearStrengthMetricKeys.AxialStrain),
        ("ShearStrength", ShearStrengthMetricKeys.ShearStrength),
        ("Mass", FieldName(ShearStrengthSampleFields.MassKey)),
        ("WaterContent", FieldName(ShearStrengthSampleFields.WaterContentKey)),
        ("DryDensity", FieldName(ShearStrengthSampleFields.DryDensityKey)),
        ("WaterContentLoss", FieldName(ShearStrengthSampleFields.WaterContentLossKey)),
        ("SampleDesc", FieldName(ShearStrengthSampleFields.SampleDescKey)),
    ];

    /// <summary>按关键字查本方法的试验单（关键字为空 = 全部；排序由存储层按创建时间倒序）。</summary>
    public Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default
    ) =>
        sheets.QueryAsync(
            ShearStrengthPersistenceModule.MethodCodeValue,
            keyword,
            cancellationToken
        );

    /// <summary>读某张单的试件明细（每行一个"**显示资源键** → 文本"，键是资源键而不是库列名）。</summary>
    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadSamplesAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var rows = sheets.ReadDetailRows(
            sheetId,
            [.. SampleColumns.Select(column => column.Column)]
        );

        IReadOnlyList<IReadOnlyDictionary<string, string>> result =
        [
            .. rows.Select(row => (IReadOnlyDictionary<string, string>)
                SampleColumns.ToDictionary(
                    column => column.DisplayKey,
                    column => Format(row[column.Column]),
                    StringComparer.Ordinal
                )),
        ];

        return Task.FromResult(result);
    }

    /// <summary>读某件试件的曲线（落库时已按 0.5mm 抽样，历史回放画的就是它）。</summary>
    public Task<IReadOnlyList<CurvePoint>> ReadCurveAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(sheets.ReadCurve(sheetId, sampleId));

    /// <summary>
    /// 本方法**不做原点修正拟合**：T 1236 的 ε1 直接按 Δh / h0 算，明细表也没有拟合列
    /// （执行页同样没有修正直线 / 修正原点可画）—— 所以历史回放一律 <c>null</c>，只画曲线与峰值点。
    /// </summary>
    public Task<CurveAnnotation?> ReadAnnotationAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<CurveAnnotation?>(null);

    /// <summary>试样信息列：按键取该字段的显示名资源键（与试样编辑页同一份声明）。</summary>
    private static string FieldName(string key) => ShearStrengthSampleFields.NameKey(key);

    /// <summary>值 → 文本：数值按报表同一套 <c>0.###</c>（不变文化），其余原样转文本。</summary>
    private static string Format(object? value) =>
        value switch
        {
            null or DBNull => string.Empty,
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
}
