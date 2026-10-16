namespace ANX.Method.Marshall;

using System.Globalization;
using ANX.Control;
using ANX.Core;
using ANX.Core.Reporting;
using ANX.Core.TestSheets;
using ANX.Storage;

/// <summary>历史数据页的数据来源：查单 / 读明细 / 读曲线 / 读曲线标注。</summary>
public sealed class MarshallSheetBrowser(TestSheetService sheets, ITestParameters parameters)
    : ITestSheetBrowser
{
    /// <summary>曲线横轴：变形（mm）。</summary>
    public string CurveXAxisKey => MarshallCurveAxes.Deformation;

    /// <summary>曲线纵轴：荷载（kN）。</summary>
    public string CurveYAxisKey => MarshallCurveAxes.Load;

    /// <summary>
    /// 试件列表要读的明细列 + **列名的显示资源键**（界面按当前语言取，界面不认识库列名）。
    /// </summary>
    private static readonly (string Column, string DisplayKey)[] SampleColumns =
    [
        ("SampleID", MarshallMetricKeys.SampleId),
        ("SampleInitialDiameter", ParameterTextKeys.Name(MarshallSampleFields.SampleInitialDiameterKey)),
        ("SampleInitialHeight", ParameterTextKeys.Name(MarshallSampleFields.SampleInitialHeightKey)),
        ("MSRaw", MarshallMetricKeys.Stability),
        ("FlowValue", MarshallMetricKeys.FlowValue),
        ("MarshallModulus", MarshallMetricKeys.MarshallModulus),
    ];

    public Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default
    ) => sheets.QueryAsync("T0709", keyword, cancellationToken);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadSamplesAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var rows = sheets.ReadDetailRows(sheetId, [.. SampleColumns.Select(column => column.Column)]);
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

    public Task<IReadOnlyList<CurvePoint>> ReadCurveAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(sheets.ReadCurve(sheetId, sampleId));

    /// <summary>
    /// 读拟合结果列拼出曲线标注（解析只有 <see cref="MarshallAnnotations"/> 那一份，
    /// 出报表与自动存图也走它）。斜率 ≤ 0（没拟合 / 老记录没有这两列）回 <c>null</c>，
    /// 界面就只画峰值点。
    /// </summary>
    public async Task<CurveAnnotation?> ReadAnnotationAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);
        var (startPercent, endPercent) = MarshallAnnotations.ReadPercents(values);

        return MarshallAnnotations.Find(
            sheets.ReadDetailRows(sheetId, MarshallAnnotations.Columns),
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
/// 产物目录：`&lt;输出根&gt;\稳定度\&lt;单号&gt;\实验报告.&lt;语种&gt;.xlsx`。
/// </summary>
public sealed class MarshallReportExportService(
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
        var templatePath = MarshallTemplates.Resolve(templatesRoot, culture);

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

        // 离屏渲染是一次文件 IO + 位图渲染：放线程池跑，别占着界面线程（这一步在"试验刚跑完"的路径上调）
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
    private MarshallReportBuilder CreateBuilder() =>
        new(sheets, new ANX.Reporting.FlexCelReportExporter(), curveImages: null, parameters);
}
