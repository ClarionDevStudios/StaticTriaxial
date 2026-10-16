namespace ANX.Method.Marshall;

using System.Globalization;
using ANX.Control;

/// <summary>
/// 曲线标注的"库 → 内存"这一层：明细表的拟合结果列（<c>TangentK</c> / <c>TangentB</c> / <c>OPrime</c>）
/// 加参数表里的拟合区间百分比，拼成 <see cref="CurveAnnotation"/> —— 与弯曲
/// <c>BendingAnnotations</c> 同款，只是列名按马歇尔明细表来。
///
/// 三条路径共用这一份：历史数据页回放（<c>MarshallSheetBrowser</c>）、导出报表、
/// 试验完成后自动存图（<c>MarshallReportBuilder</c>）。
/// 没有拟合的试件（判定失败、老记录没存拟合列 → 斜率 ≤ 0）回 <c>null</c>，图上只画峰值点。
/// </summary>
public static class MarshallAnnotations
{
    /// <summary>拼标注要读的明细列。</summary>
    public static readonly string[] Columns =
    [
        "SampleID",
        "TangentK",
        "TangentB",
        "OPrime",
    ];

    /// <summary>按试件号在明细行里找那一行并拼出标注；没有拟合回 <c>null</c>。</summary>
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
        _ = TryDouble(row["OPrime"], out var oPrime);

        return new CurveAnnotation(tangentK, tangentB, oPrime, startPercent, endPercent);
    }

    /// <summary>
    /// 拟合区间百分比（占峰值荷载的百分数）：从参数表的值里取，取不到或不是正数就用默认 10 / 80。
    /// **已知不足**（与弯曲同一条，2026-10-15 记录）：这两个值没有随明细落库，读的是**当前**参数表取值；
    /// 改过区间再看老记录，两端标记会跟着动。要彻底对齐得给明细表加列（另立一项）。
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
            ReadPercent(
                values,
                MachineParameterCatalog.OriginCorrectionStartKey,
                CurveAnnotation.DefaultStartPercent
            ),
            ReadPercent(
                values,
                MachineParameterCatalog.OriginCorrectionEndKey,
                CurveAnnotation.DefaultEndPercent
            )
        );
    }

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
