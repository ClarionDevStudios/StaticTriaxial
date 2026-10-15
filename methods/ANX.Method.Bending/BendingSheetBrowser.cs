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
    ) => Task.FromResult(sheets.ReadCurve(sheetId, sampleId));

    /// <summary>
    /// 读拟合结果列拼出曲线标注（解析只有 <see cref="BendingAnnotations"/> 那一份，
    /// 出报表与自动存图也走它）。斜率 ≤ 0（没拟合 / 关掉了原点修正 / 老记录没有这两列）回 <c>null</c>，
    /// 界面就只画峰值点 —— 与试验执行页"关掉原点修正就不画"的口径一致。
    /// </summary>
    public async Task<CurveAnnotation?> ReadAnnotationAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);
        var (startPercent, endPercent) = BendingAnnotations.ReadPercents(values);

        return BendingAnnotations.Find(
            sheets.ReadDetailRows(sheetId, BendingAnnotations.Columns),
            sampleId,
            startPercent,
            endPercent
        );
    }

    private static string Format(object? value) =>
        value switch
        {
            null => string.Empty,
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
}

/// <summary>
/// 报表导出的方法级实现：选模板（按语种）→ 装配 → 出 Excel 与曲线 PNG。
/// <paramref name="texts"/> 由宿主接上界面的本地化服务，用于曲线图里的轴名与图题。
/// <paramref name="parameters"/> 用来取拟合区间百分比（出图的标注要用）；不给就用默认 10 / 80。
/// </summary>
public sealed class BendingReportExportService(
    TestSheetService sheets,
    string reportsRoot,
    ITextResolver? texts = null,
    ITestParameters? parameters = null
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

        var builder = CreateBuilder();
        var result = await builder
            .BuildAsync(sheetId, templatePath, reportsRoot, culture, texts, cancellationToken)
            .ConfigureAwait(false);

        return result.ReportPath;
    }

    /// <inheritdoc />
    public Task<string?> SaveCurveImageAsync(
        string sheetId,
        string sampleId,
        string culture,
        CancellationToken cancellationToken = default
    )
    {
        var builder = CreateBuilder();

        // ScottPlot 的离屏渲染不依赖 WPF，但仍是一次文件 IO + 位图渲染（几十毫秒）：
        // 放线程池跑，别占着界面线程（这一步是在"试验刚跑完"的路径上调的）。
        return Task.Run(
            () => builder.SaveCurveImageAsync(
                sheetId,
                sampleId,
                reportsRoot,
                culture,
                texts,
                cancellationToken
            ),
            cancellationToken
        );
    }

    /// <summary>装配器（导出报表与只存曲线图两处共用同一套依赖）。</summary>
    private BendingReportBuilder CreateBuilder() =>
        new(sheets, new ANX.Reporting.FlexCelReportExporter(), curveImages: null, parameters);
}
