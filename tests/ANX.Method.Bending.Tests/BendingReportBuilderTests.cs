namespace ANX.Method.Bending.Tests;

using ANX.Core;
using ANX.Core.Reporting;
using ANX.Reporting;
using ANX.Storage;
using FlexCel.XlsAdapter;
using Microsoft.Data.Sqlite;

/// <summary>
/// 报表装配的端到端：建库 → 写入试验单与两个试件（含曲线）→ 装配 → 出 Excel 与曲线 PNG →
/// 重新打开断言标签被填上、PNG 真的生成了。
/// </summary>
public sealed class BendingReportBuilderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-bend-report-{Guid.NewGuid():N}"
    );

    public BendingReportBuilderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 应出带数据的报表与每试件一张曲线图()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("T0715", "G15 高速 K12+300", "WT");
        await service.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["TestTemperature"] = 15,
                ["LoadRate"] = 50,
                ["SpanLength"] = 200,
                ["EffectiveCount"] = 2,
                ["FlexuralStrengthAvg"] = 73.79,
                ["MaxBendingStrainAvg"] = 23663,
            }
        );

        var curve = Enumerable.Range(0, 120).Select(i => new CurvePoint(i * 0.05, i * 0.075)).ToArray();
        foreach (var sampleId in new[] { "1", "2" })
        {
            await service.AppendSampleAsync(
                sheetId,
                sampleId,
                curve,
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleWidth"] = 30,
                    ["SampleHeight"] = 35,
                    ["PeakLoadValue"] = 9.0,
                    ["CorrectedDeflection"] = 4.5,
                    ["FlexuralStrength"] = 73.47,
                    ["MaxBendingTensileStrain"] = 23625,
                }
            );
        }

        var templatePath = Path.Combine(_directory, "template.xlsx");
        BuildTemplate(templatePath);

        var builder = new BendingReportBuilder(service, new FlexCelReportExporter());
        var result = await builder.BuildAsync(sheetId, templatePath, Path.Combine(_directory, "out"));

        Assert.True(File.Exists(result.ReportPath), "报表文件应生成");
        Assert.Equal(2, result.CurveImagePaths.Count);
        Assert.All(result.CurveImagePaths, p => Assert.True(new FileInfo(p).Length > 1000, $"{p} 应有内容"));

        var workbook = new XlsFile();
        workbook.Open(result.ReportPath);

        Assert.Equal("G15 高速 K12+300", workbook.GetCellValue(1, 2)); // {{Sheet.ProjectName}}
        Assert.Equal("73.79", workbook.GetCellValue(2, 2)); // {{Result.FlexuralStrengthAvg}}
        Assert.Equal("1", workbook.GetCellValue(3, 2)); // 明细第一行
        Assert.Equal("2", workbook.GetCellValue(4, 2)); // 明细第二行
        Assert.Equal("73.47", workbook.GetCellValue(3, 4)); // 明细单件强度
    }

    /// <summary>
    /// 曲线图里的图题与轴名要按**报表语种**取译文（不是界面当前语言）——
    /// 这里用假解析器记录"被问了哪些键、哪个语种"来验证这条链路。
    /// </summary>
    [Fact]
    public async Task 曲线图的图题与轴名应按报表语种取资源键()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("T0715", "G15 高速 K12+300", "WT");
        await service.AppendSampleAsync(
            sheetId,
            "1",
            [new CurvePoint(0, 0), new CurvePoint(1, 1), new CurvePoint(2, 2)],
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["SampleWidth"] = 30,
                ["SampleHeight"] = 35,
                ["PeakLoadValue"] = 9.0,
                ["CorrectedDeflection"] = 4.5,
                ["FlexuralStrength"] = 73.47,
                ["MaxBendingTensileStrain"] = 23625,
            }
        );

        var templatePath = Path.Combine(_directory, "template.xlsx");
        BuildTemplate(templatePath);

        var texts = new RecordingTextResolver();
        var builder = new BendingReportBuilder(service, new FlexCelReportExporter());

        _ = await builder.BuildAsync(
            sheetId,
            templatePath,
            Path.Combine(_directory, "out"),
            "en-US",
            texts
        );

        Assert.Contains((BendingCurveAxes.Deflection, "en-US"), texts.Requests);
        Assert.Contains((BendingCurveAxes.Load, "en-US"), texts.Requests);
        Assert.Contains((BendingMetricKeys.CurveTitle, "en-US"), texts.Requests);
    }

    /// <summary>假解析器：只记录"被问了哪些键、哪个语种"。</summary>
    private sealed class RecordingTextResolver : ITextResolver
    {
        public List<(string Key, string Culture)> Requests { get; } = [];

        public string Get(string key, string culture)
        {
            Requests.Add((key, culture));
            return $"[{culture}]{key}";
        }
    }

    /// <summary>
    /// 明细行数**不再有上限**（用户 2026-10-15："不限制试样信息行数"）：模板里排不下就**自动插行**，
    /// 而不是像上一版那样明确拒导。这个用例用的是一张只有 1 行明细区的手工模板。
    /// </summary>
    [Fact]
    public async Task 明细超过模板行数时应自动插行而不是拒导()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("T0715", "G15 高速 K12+300", "WT");

        const int count = 18;
        for (var index = 1; index <= count; index++)
        {
            await service.AppendSampleAsync(
                sheetId,
                // 零填充试件号：明细按试件号取，写 "1".."18" 会排成 1,10,11,…,2,3…（踩过）
                $"S{index:000}",
                [new CurvePoint(0, 0), new CurvePoint(1, 1), new CurvePoint(2, 2)],
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleWidth"] = 30,
                    ["SampleHeight"] = 35,
                    ["FlexuralStrength"] = 70 + index,
                }
            );
        }

        var templatePath = Path.Combine(_directory, "template.xlsx");
        BuildTemplate(templatePath);

        var builder = new BendingReportBuilder(service, new FlexCelReportExporter());
        var result = await builder.BuildAsync(sheetId, templatePath, Path.Combine(_directory, "out"));

        var workbook = new XlsFile();
        workbook.Open(result.ReportPath);

        // 模板的明细区只有第 3 行一行 → 18 件要插 17 行；1..18 依次落在第 3 行往下
        Assert.Equal("S001", workbook.GetCellValue(3, 2));
        Assert.Equal("S018", workbook.GetCellValue(3 + count - 1, 2));
        Assert.Equal("88", workbook.GetCellValue(3 + count - 1, 4)); // 70 + 18
    }

    private static void BuildTemplate(string path)
    {
        var template = new XlsFile();
        template.NewFile();
        template.SetCellValue(1, 1, "项目名称");
        template.SetCellValue(1, 2, "{{Sheet.ProjectName}}");
        template.SetCellValue(2, 1, "弯拉强度平均值(MPa)");
        template.SetCellValue(2, 2, "{{Result.FlexuralStrengthAvg}}");
        template.SetCellValue(3, 1, "试件号");
        template.SetCellValue(3, 2, "{{Detail.SampleID}}");
        template.SetCellValue(3, 3, "单件强度(MPa)");
        template.SetCellValue(3, 4, "{{Detail.FlexuralStrength}}");
        template.Save(path);
    }

    private TestSheetService CreateService()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();

        return new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
    }
}
