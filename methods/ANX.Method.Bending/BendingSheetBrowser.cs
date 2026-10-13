namespace ANX.Method.Bending;

using System.Globalization;
using ANX.Control;
using ANX.Core;
using ANX.Core.Reporting;
using ANX.Core.TestSheets;
using ANX.Storage;

/// <summary>历史数据页的数据来源：查单 / 读明细 / 读曲线 / 读曲线标注。</summary>
public sealed class BendingSheetBrowser(TestSheetService sheets, ITestParameters parameters)
    : ITestSheetBrowser
{
    /// <summary>曲线横轴：跨中挠度。</summary>
    public string CurveXAxisKey => BendingCurveAxes.Deflection;

    /// <summary>曲线纵轴：荷载。</summary>
    public string CurveYAxisKey => BendingCurveAxes.Load;

    private static readonly string[] SampleColumns =
    [
        "SampleID",
        "SampleWidth",
        "SampleHeight",
        "PeakLoadValue",
        "CorrectedDeflection",
        "FlexuralStrength",
        "MaxBendingTensileStrain",
    ];

    /// <summary>读曲线标注要用的明细列（判定器落库的拟合结果列）。</summary>
    private static readonly string[] AnnotationColumns =
    [
        "SampleID",
        "TangentK",
        "TangentB",
        "OriginCorrection",
    ];

    public Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default
    ) => sheets.QueryAsync("T0715", keyword, cancellationToken);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadSamplesAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var rows = sheets.ReadDetailRows(sheetId, SampleColumns);
        IReadOnlyList<IReadOnlyDictionary<string, string>> result =
        [
            .. rows.Select(row => (IReadOnlyDictionary<string, string>)
                SampleColumns.ToDictionary(
                    column => column,
                    column => Format(row[column]),
                    StringComparer.Ordinal
                )),
        ];

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<CurvePoint>> ReadCurveAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(sheets.ReadCurve($"{sheetId}-{sampleId}"));

    /// <summary>
    /// 读拟合结果列拼出曲线标注。斜率 ≤ 0（没拟合 / 关掉了原点修正 / 老记录没有这两列）
    /// 回 <c>null</c>，界面就只画峰值点 —— 与试验执行页"关掉原点修正就不画"的口径一致。
    ///
    /// **待改进**：拟合区间百分比（10%/80%）没有落库，这里按**当前参数表**的取值显示；
    /// 若用户改过区间再看老记录，两端标记会跟着动。要彻底对齐得把这两个值也写进明细表。
    /// </summary>
    public async Task<CurveAnnotation?> ReadAnnotationAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var row = sheets
            .ReadDetailRows(sheetId, AnnotationColumns)
            .FirstOrDefault(candidate =>
                string.Equals(
                    Convert.ToString(candidate["SampleID"], CultureInfo.InvariantCulture),
                    sampleId,
                    StringComparison.Ordinal
                )
            );

        if (row is null
            || !TryDouble(row["TangentK"], out var tangentK)
            || tangentK <= 0)
        {
            return null;
        }

        _ = TryDouble(row["TangentB"], out var tangentB);
        _ = TryDouble(row["OriginCorrection"], out var originCorrection);

        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);

        return new CurveAnnotation(
            tangentK,
            tangentB,
            originCorrection,
            ReadPercent(values, BendingParameters.OriginCorrectionStartKey, CurveAnnotation.DefaultStartPercent),
            ReadPercent(values, BendingParameters.OriginCorrectionEndKey, CurveAnnotation.DefaultEndPercent)
        );
    }

    private static string Format(object? value) =>
        value switch
        {
            null => string.Empty,
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static bool TryDouble(object? value, out double number) =>
        double.TryParse(
            Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number
        );

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
}

/// <summary>
/// 报表导出的方法级实现：选模板（按语种）→ 装配 → 出 Excel 与曲线 PNG。
/// <paramref name="texts"/> 由宿主接上界面的本地化服务，用于曲线图里的轴名与图题。
/// </summary>
public sealed class BendingReportExportService(
    TestSheetService sheets,
    string reportsRoot,
    ITextResolver? texts = null
) : IReportExportService
{
    public async Task<string> ExportAsync(
        string sheetId,
        string culture,
        CancellationToken cancellationToken = default
    )
    {
        var templatesRoot = Path.Combine(reportsRoot, "Template");
        var templatePath = BendingTemplates.Resolve(templatesRoot, culture);

        var builder = new BendingReportBuilder(sheets, new ANX.Reporting.FlexCelReportExporter());
        var result = await builder
            .BuildAsync(sheetId, templatePath, reportsRoot, culture, texts, cancellationToken)
            .ConfigureAwait(false);

        return result.ReportPath;
    }
}
