namespace ANX.Reporting.Tests;

using ANX.Core.Reporting;
using FlexCel.XlsAdapter;

/// <summary>
/// 标签制导出：模板里写占位符 → 导出 → 重新打开断言单元格内容。
/// 重点验证"代码不认坐标、只认标签"，以及明细行按行展开。
/// </summary>
public sealed class FlexCelReportExporterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-report-{Guid.NewGuid():N}"
    );

    public FlexCelReportExporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 单值标签与明细标签都应被填上()
    {
        var templatePath = Path.Combine(_directory, "template.xlsx");
        var outputPath = Path.Combine(_directory, "out.xlsx");

        // 造一个标签制模板：A 列是固定文字，B 列是标签
        var template = new XlsFile();
        template.NewFile();
        template.SetCellValue(1, 1, "项目名称");
        template.SetCellValue(1, 2, "{{Sheet.ProjectName}}");
        template.SetCellValue(2, 1, "试件号");
        template.SetCellValue(2, 2, "{{Detail.SampleID}}");
        template.SetCellValue(2, 3, "抗弯拉强度(MPa)");
        template.SetCellValue(2, 4, "{{Detail.FlexuralStrength}}");
        template.Save(templatePath);

        var exporter = new FlexCelReportExporter();
        await exporter.ExportAsync(
            new ReportRequest(
                MethodCode: "T0715",
                TemplatePath: templatePath,
                OutputPath: outputPath,
                Fields: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Sheet.ProjectName"] = "G15 高速 K12+300",
                },
                DetailRows:
                [
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Detail.SampleID"] = "1",
                        ["Detail.FlexuralStrength"] = "73.47",
                    },
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["Detail.SampleID"] = "2",
                        ["Detail.FlexuralStrength"] = "74.10",
                    },
                ]
            )
        );

        var result = new XlsFile();
        result.Open(outputPath);

        Assert.Equal("项目名称", result.GetCellValue(1, 1));
        Assert.Equal("G15 高速 K12+300", result.GetCellValue(1, 2));

        // 明细第 1 条写在模板行，第 2 条写在下一行
        Assert.Equal("1", result.GetCellValue(2, 2));
        Assert.Equal("2", result.GetCellValue(3, 2));
        Assert.Equal("73.47", result.GetCellValue(2, 4));
        Assert.Equal("74.10", result.GetCellValue(3, 4));
    }

    [Fact]
    public async Task 目标文件已存在时应覆盖而不是报错()
    {
        var templatePath = Path.Combine(_directory, "t3.xlsx");
        var outputPath = Path.Combine(_directory, "o3.xlsx");

        var template = new XlsFile();
        template.NewFile();
        template.SetCellValue(1, 1, "{{Sheet.ProjectName}}");
        template.Save(templatePath);

        // 用户会反复导同一张单（执行页"打印报表"、历史页"导出"）：第二次必须覆盖，不能报"文件已存在"
        var exporter = new FlexCelReportExporter();
        await exporter.ExportAsync(Request(templatePath, outputPath, "第一次"));
        await exporter.ExportAsync(Request(templatePath, outputPath, "第二次"));

        var result = new XlsFile();
        result.Open(outputPath);
        Assert.Equal("第二次", result.GetCellValue(1, 1));

        // 落盘走"临时文件 + 原子替换"，收尾不留 .tmp
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    private static ReportRequest Request(string templatePath, string outputPath, string projectName) =>
        new(
            "T0715",
            templatePath,
            outputPath,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Sheet.ProjectName"] = projectName,
            },
            []
        );

    [Fact]
    public void 没有对应标签的单元格应原样保留()
    {
        var templatePath = Path.Combine(_directory, "t2.xlsx");
        var outputPath = Path.Combine(_directory, "o2.xlsx");

        var template = new XlsFile();
        template.NewFile();
        template.SetCellValue(1, 1, "报告");
        template.SetCellValue(2, 1, "{{Result.Nope}}");
        template.Save(templatePath);

        new FlexCelReportExporter()
            .ExportAsync(
                new ReportRequest("T0715", templatePath, outputPath, new Dictionary<string, string>(), [])
            )
            .GetAwaiter()
            .GetResult();

        var result = new XlsFile();
        result.Open(outputPath);

        Assert.Equal("报告", result.GetCellValue(1, 1));
        Assert.Equal("{{Result.Nope}}", result.GetCellValue(2, 1)); // 未提供的标签保持原样，便于发现漏填
    }
}
