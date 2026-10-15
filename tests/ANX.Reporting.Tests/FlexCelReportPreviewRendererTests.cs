namespace ANX.Reporting.Tests;

using ANX.Core.Reporting;
using FlexCel.Core;
using FlexCel.XlsAdapter;

/// <summary>
/// 报表预览渲染：把**已生成的报表文件**渲成一张连续长图（用户 2026-10-15 的"打印预览"靠它；
/// 预览**不做分页**，分页能力留给将来的"按页预览/导 PDF"）。
/// 这里盯两件事：是合法 PNG、是**一张连续长图**（内容多时更高，而不是被裁成一页）。
/// </summary>
public sealed class FlexCelReportPreviewRendererTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-preview-{Guid.NewGuid():N}"
    );

    public FlexCelReportPreviewRendererTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 报表应渲成一张连续长图()
    {
        var templatePath = Path.Combine(_directory, "template.xlsx");
        var reportPath = Path.Combine(_directory, "report.xlsx");

        // 模板：A4 横向 + 一行明细标签（明细块只有 1 行 → 60 件要靠插行）
        var template = new XlsFile();
        template.NewFile();
        template.SetCellValue(1, 1, "{{Sheet.ProjectName}}");
        template.SetCellValue(2, 1, "试件号");
        template.SetCellValue(2, 2, "{{Detail.SampleID}}");
        template.PrintPaperSize = TPaperSize.A4;
        template.PrintLandscape = true;
        template.Save(templatePath);

        var detailRows = Enumerable
            .Range(1, 60)
            .Select(index => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                StringComparer.Ordinal
            )
            {
                ["Detail.SampleID"] = $"S{index:000}",
            })
            .ToArray();

        await new FlexCelReportExporter().ExportAsync(
            new ReportRequest(
                "T0715",
                templatePath,
                reportPath,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Sheet.ProjectName"] = "G15 高速 K12+300",
                },
                detailRows
            )
        );

        var png = new FlexCelReportPreviewRenderer().RenderPng(reportPath, dpi: 96);

        Assert.True(png.Length > 1000, "渲染结果不该是空图");
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

        // 一张**连续**长图：60 行明细（每行 15pt）渲出来远高于一页 A4 横向（96dpi 下约 794px）
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        Assert.True(height > 1000, $"应是一张连续长图（内容没被裁成一页），实际高 {height}px");
    }
}
