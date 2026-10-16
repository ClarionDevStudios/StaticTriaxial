namespace STS.Method.ShearStrength;

using STS.Control;
using STS.Core.Reporting;
using STS.Reporting;
using STS.Storage;

/// <summary>
/// 报表导出的方法级实现：选模板（按语种）→ 装配（<see cref="ShearStrengthReportBuilder"/>）→
/// 出 Excel 与曲线 PNG。模板由 <see cref="ShearStrengthTemplates"/> 按语种确保（缺 / 旧自动换代）。
/// <paramref name="texts"/> 由宿主接上本地化服务，用于曲线图里的图题与轴名。
/// </summary>
#pragma warning disable CS9113 // parameters：留给后续（本期装配不用方法级参数）
public sealed class ShearStrengthReportExportService(
    TestSheetService sheets,
    string reportRoot,
    ITextResolver texts,
    ShearStrengthParameters parameters
) : IReportExportService
#pragma warning restore CS9113
{
    /// <inheritdoc />
    public async Task<string> ExportAsync(
        string sheetId,
        string culture,
        CancellationToken cancellationToken = default
    )
    {
        var templatesRoot = Path.Combine(reportRoot, "Template");
        var templatePath = ShearStrengthTemplates.Resolve(templatesRoot, culture);

        var result = await CreateBuilder()
            .BuildAsync(sheetId, templatePath, reportRoot, culture, texts, cancellationToken)
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

        // 离屏渲染不依赖 WPF，但仍是一次文件 IO + 位图渲染（几十毫秒）：
        // 放线程池跑，别占着界面线程（这一步是在"试验刚跑完"的路径上调的）。
        return Task.Run(
            () => builder.SaveCurveImageAsync(
                sheetId,
                sampleId,
                reportRoot,
                culture,
                texts,
                cancellationToken
            ),
            cancellationToken
        );
    }

    /// <summary>装配器（导出报表与只存曲线图两处共用同一套依赖）。</summary>
    private ShearStrengthReportBuilder CreateBuilder() =>
        new(sheets, new FlexCelReportExporter(), curveImages: null);
}
