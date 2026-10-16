namespace STS.Method.ShearStrength.Tests;

using STS.Storage;
using FlexCel.XlsAdapter;

/// <summary>
/// 报表链路（报告 6.1~6.8，中英两套）：出厂模板代码生成（幂等 / 语种回落）→
/// 装配导出（关键字段 + 明细 + 曲线图）→ 两份语种文件互不覆盖。
/// </summary>
public sealed class ShearStrengthReportTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sts-report-{Guid.NewGuid():N}"
    );

    private readonly TempSheetDatabase _database = new();

    public ShearStrengthReportTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        _database.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 出厂模板应在中英各写一份且幂等()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");

        ShearStrengthTemplates.EnsureDefaultTemplates(templatesRoot);
        Assert.True(File.Exists(Path.Combine(templatesRoot, "ShearStrength.zh-CN.xlsx")));
        Assert.True(File.Exists(Path.Combine(templatesRoot, "ShearStrength.en-US.xlsx")));

        // 幂等：再调一次不重写、不报错
        ShearStrengthTemplates.EnsureDefaultTemplates(templatesRoot);

        Assert.Equal(
            Path.Combine(templatesRoot, "ShearStrength.en-US.xlsx"),
            ShearStrengthTemplates.Resolve(templatesRoot, "en-US")
        );
        // 语种不认识时回落到中文
        Assert.Equal(
            Path.Combine(templatesRoot, "ShearStrength.zh-CN.xlsx"),
            ShearStrengthTemplates.Resolve(templatesRoot, "ja-JP")
        );
    }

    [Fact]
    public async Task 导出应生成中英两份报表并填入关键内容()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var sheetId = await execution.CreateSheetAsync("G15 高速 K12+300", "WT");
        await SeedCompletedSheetAsync(service, sheetId);

        var exportRoot = Path.Combine(_directory, "Report");
        var exportService = new ShearStrengthReportExportService(
            service,
            exportRoot,
            texts: null!,
            parameters: new ShearStrengthParameters()
        );

        var zhPath = await exportService.ExportAsync(sheetId, "zh-CN");
        var enPath = await exportService.ExportAsync(sheetId, "en-US");

        Assert.True(File.Exists(zhPath), $"中文报表应生成：{zhPath}");
        Assert.True(File.Exists(enPath), $"英文报表应生成：{enPath}");
        Assert.NotEqual(zhPath, enPath);

        // 关键内容进表：标题 / 项目名称 / 明细首行 / 参数区
        var zhTexts = DumpStrings(zhPath);
        Assert.Contains("抗剪强度试验（静三轴法）报表", zhTexts, StringComparison.Ordinal);
        Assert.Contains("G15 高速 K12+300", zhTexts, StringComparison.Ordinal);
        Assert.Contains("S001", zhTexts, StringComparison.Ordinal);
        Assert.Contains("11.547", zhTexts, StringComparison.Ordinal); // c（示例值）

        var enTexts = DumpStrings(enPath);
        Assert.Contains("Shear Strength Test (Static Triaxial) Report", enTexts, StringComparison.Ordinal);
        Assert.Contains("G15 高速 K12+300", enTexts, StringComparison.Ordinal);

        // 曲线图逐件出图并嵌进报表
        var imagePath = Path.Combine(exportRoot, "抗剪强度", sheetId, "S001.png");
        Assert.True(File.Exists(imagePath), $"曲线图应生成：{imagePath}");

        var workbook = new XlsFile();
        workbook.Open(zhPath);
        Assert.True(workbook.ImageCount >= 1, "报表里应嵌入曲线图");
    }

    /// <summary>造一张"已完成"的单：3 级 × 3 件 + 收尾参数（c / φ / r + 分级结果）。</summary>
    private static async Task SeedCompletedSheetAsync(TestSheetService service, string sheetId)
    {
        var index = 0;

        foreach (var (sigma3, strength) in new[] { (30.0, 130.0), (60.0, 220.0), (100.0, 340.0) })
        {
            for (var i = 0; i < 3; i++)
            {
                index++;
                var sampleId = $"S{index:D3}";

                await service.UpsertSampleInfoAsync(
                    sheetId,
                    sampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["ConfiningPressure"] = sigma3,
                        ["InitialHeight"] = 306,
                        ["InitialDiameter"] = 100,
                        ["Mass"] = 3700,
                        ["WaterContent"] = 5.2,
                        ["DryDensity"] = 2.21,
                        ["WaterContentLoss"] = 0.3,
                    }
                );

                var curve = new[]
                {
                    new STS.Core.CurvePoint(0, 0),
                    new STS.Core.CurvePoint(1, 1.5),
                    new STS.Core.CurvePoint(2, 2.8),
                    new STS.Core.CurvePoint(3, 3.6),
                };

                await service.UpdateDetailFieldsAsync(
                    sheetId,
                    sampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["MaxAxialLoad"] = 3800,
                        ["AxialStrain"] = 7.7,
                        ["ShearStrength"] = strength,
                        ["StoppedAt15PercentStrain"] = 0,
                        ["TestStatus"] = 1,
                    },
                    curve
                );
            }
        }

        await service.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["CohesionKPa"] = 11.547,
                ["FrictionAngleDeg"] = 30.0,
                ["EnvelopeCorrelation"] = 1.0,
                ["EnvelopeReliable"] = 1.0,
                ["LevelResultsJson"] =
                    "[{\"sigma3\":30,\"meanShearStrength\":130,\"count\":3},"
                    + "{\"sigma3\":60,\"meanShearStrength\":220,\"count\":3},"
                    + "{\"sigma3\":100,\"meanShearStrength\":340,\"count\":3}]",
            }
        );
        await service.CompleteAsync(sheetId);
    }

    /// <summary>把工作簿里所有文本单元格拼起来（断言"某内容进表"用）。</summary>
    private static string DumpStrings(string path)
    {
        var workbook = new XlsFile();
        workbook.Open(path);

        var text = new System.Text.StringBuilder();
        for (var row = 1; row <= workbook.RowCount; row++)
        {
            for (var col = 1; col <= workbook.ColCount; col++)
            {
                if (workbook.GetCellValue(row, col) is string value)
                {
                    text.Append(value).Append('\n');
                }
            }
        }

        return text.ToString();
    }
}
