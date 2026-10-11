namespace ANX.Method.Bending;

using System.Globalization;
using ANX.Control;
using ANX.Core;
using ANX.Core.TestSheets;
using ANX.Storage;

/// <summary>历史数据页的数据来源：查单 / 读明细 / 读曲线。</summary>
public sealed class BendingSheetBrowser(TestSheetService sheets) : ITestSheetBrowser
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
    ) => Task.FromResult(sheets.ReadCurve($"{sheetId}-{sampleId}"));

    private static string Format(object? value) =>
        value switch
        {
            null => string.Empty,
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
}

/// <summary>报表导出的方法级实现：选模板（按语种）→ 装配 → 出 Excel 与曲线 PNG。</summary>
public sealed class BendingReportExportService(TestSheetService sheets, string reportsRoot)
    : IReportExportService
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
            .BuildAsync(sheetId, templatePath, reportsRoot, culture, cancellationToken)
            .ConfigureAwait(false);

        return result.ReportPath;
    }
}
