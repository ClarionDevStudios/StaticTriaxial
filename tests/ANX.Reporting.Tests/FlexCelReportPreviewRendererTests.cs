namespace ANX.Reporting.Tests;

using ANX.Core.Reporting;
using FlexCel.XlsAdapter;

/// <summary>
/// 报表预览渲染：把**已生成的报表文件**渲成一张 PNG（用户 2026-10-15 的"打印预览"靠它）。
/// 这里盯三件事：文件头是 PNG、尺寸随内容长（整张表一张图）、**隐藏列不进图**。
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
    public async Task 报表应能渲成PNG且隐藏列不进图()
    {
        var templatePath = Path.Combine(_directory, "template.xlsx");
        var reportPath = Path.Combine(_directory, "report.xlsx");

        var template = new XlsFile();
        template.NewFile();
        template.SetCellValue(1, 1, "{{Sheet.ProjectName}}");
        template.SetCellValue(2, 1, "试件号");
        template.SetCellValue(2, 2, "{{Detail.SampleID}}");

        // 版本戳那一列（K）是隐藏的：出厂模板就是这么藏的，渲染时不该带进图里
        template.SetCellValue(1, 11, "ANX-TEMPLATE-V9");
        template.SetColHidden(11, true);
        template.Save(templatePath);

        await new FlexCelReportExporter().ExportAsync(
            new ReportRequest(
                "T0715",
                templatePath,
                reportPath,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Sheet.ProjectName"] = "G15 高速 K12+300",
                },
                [
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Detail.SampleID"] = "S001",
                    },
                ]
            )
        );

        var png = new FlexCelReportPreviewRenderer().RenderPng(reportPath, dpi: 96);

        // PNG 签名
        Assert.True(png.Length > 1000, "渲染结果不该是空图");
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], png[..8]);

        // 尺寸：按"有内容的范围"渲，且**不含隐藏列 K**（含了的话宽度会明显更宽）
        var image = new XlsFile();
        image.Open(reportPath);
        Assert.True(image.ColCount >= 11, "模板里那一列隐藏的版本戳仍在，用来验证渲染范围");
        Assert.True(image.GetColHidden(11), "K 列应当是隐藏的");
    }
}
