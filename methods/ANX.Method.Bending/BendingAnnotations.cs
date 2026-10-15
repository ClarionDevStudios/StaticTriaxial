namespace ANX.Method.Bending;

using System.Globalization;
using ANX.Control;

/// <summary>
/// 曲线标注的"库 → 内存"这一层：明细表的拟合结果列（<c>TangentK</c> / <c>TangentB</c> /
/// <c>OriginCorrection</c>）加参数表里的拟合区间百分比，拼成 <see cref="CurveAnnotation"/>。
///
/// **只有这一份**，三条路径都走它：
/// <list type="bullet">
///   <item>历史数据页回放（<see cref="BendingSheetBrowser.ReadAnnotationAsync"/>）；</item>
///   <item>导出报表与"试验后自动存图"（<see cref="BendingReportBuilder"/>）。</item>
/// </list>
/// 前一条路径早就有了，后一条此前**根本没读标注** —— 于是"实时曲线画了修正直线 / 修正原点 /
/// 拟合区间两端，存盘的 PNG 里没有"（用户 2026-10-15 报的缺陷）。
/// </summary>
public static class BendingAnnotations
{
    /// <summary>拼标注要读的明细列。</summary>
    public static readonly string[] Columns =
    [
        "SampleID",
        "TangentK",
        "TangentB",
        "OriginCorrection",
    ];

    /// <summary>
    /// 按试件号在明细行里找那一行并拼出标注。
    /// 没有拟合的（关掉了原点修正 / 判定失败 / 老记录没有这两列 → 斜率 ≤ 0）回 <c>null</c>，
    /// 出图时便只画峰值点 —— 与试验执行页"关掉原点修正就不画"的口径一致。
    /// </summary>
    public static CurveAnnotation? Find(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string sampleId,
        double startPercent = CurveAnnotation.DefaultStartPercent,
        double endPercent = CurveAnnotation.DefaultEndPercent
    )
    {
        ArgumentNullException.ThrowIfNull(rows);

        var row = rows.FirstOrDefault(candidate =>
            string.Equals(Format(candidate["SampleID"]), sampleId, StringComparison.Ordinal)
        );

        if (row is null || !TryDouble(row["TangentK"], out var tangentK) || tangentK <= 0)
        {
            return null;
        }

        _ = TryDouble(row["TangentB"], out var tangentB);
        _ = TryDouble(row["OriginCorrection"], out var originCorrection);

        return new CurveAnnotation(tangentK, tangentB, originCorrection, startPercent, endPercent);
    }

    /// <summary>
    /// 拟合区间百分比（占峰值荷载的百分数）：从参数表的值里取，取不到或不是正数就用默认 10 / 80。
    ///
    /// **已知不足**（2026-10-15）：这两个值没有随明细落库，所以这里读的是**当前**参数表的取值；
    /// 用户改过区间再看老记录，两端标记会跟着动。要彻底对齐得给明细表加列（另立一项）。
    /// </summary>
    public static (double Start, double End) ReadPercents(
        IReadOnlyDictionary<string, string>? values
    )
    {
        if (values is null)
        {
            return (CurveAnnotation.DefaultStartPercent, CurveAnnotation.DefaultEndPercent);
        }

        return (
            ReadPercent(values, BendingParameters.OriginCorrectionStartKey, CurveAnnotation.DefaultStartPercent),
            ReadPercent(values, BendingParameters.OriginCorrectionEndKey, CurveAnnotation.DefaultEndPercent)
        );
    }

    /// <summary>读一个百分比参数；取不到或不是正数就用兜底值（区间必须为正）。</summary>
    private static double ReadPercent(
        IReadOnlyDictionary<string, string> values,
        string key,
        double fallback
    ) =>
        values.TryGetValue(key, out var text)
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
            ? parsed
            : fallback;

    private static string Format(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

    private static bool TryDouble(object? value, out double number) =>
        double.TryParse(
            Format(value),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number
        );
}
