namespace ANX.Method.Bending.Tests;

using ANX.Core;
using ANX.Storage;
using FlexCel.XlsAdapter;
using Microsoft.Data.Sqlite;

/// <summary>
/// 出厂模板（中英两套）与"从历史页导出报表"这条链路：
/// 模板不存在时程序自己写出 → 按语种选模板 → 装配 → 出 Excel。
/// </summary>
public sealed class BendingReportTemplateTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-tpl-{Guid.NewGuid():N}"
    );

    public BendingReportTemplateTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 出厂模板应在中英各写一份且幂等()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");

        BendingTemplates.EnsureDefaultTemplates(templatesRoot);
        Assert.True(File.Exists(Path.Combine(templatesRoot, "Bending.zh-CN.xlsx")));
        Assert.True(File.Exists(Path.Combine(templatesRoot, "Bending.en-US.xlsx")));

        // 幂等：再调一次不重写、不报错
        BendingTemplates.EnsureDefaultTemplates(templatesRoot);

        Assert.Equal(
            Path.Combine(templatesRoot, "Bending.en-US.xlsx"),
            BendingTemplates.Resolve(templatesRoot, "en-US")
        );
        // 语种不认识时回落到中文
        Assert.Equal(
            Path.Combine(templatesRoot, "Bending.zh-CN.xlsx"),
            BendingTemplates.Resolve(templatesRoot, "ja-JP")
        );
    }

    [Fact]
    public async Task 中英两套模板都应能导出带数据的报表()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0715", "G15 高速 K12+300", "WT");
        await sheets.UpdateMainFieldsAsync(
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

        var curve = Enumerable.Range(0, 60).Select(i => new CurvePoint(i * 0.1, i * 0.15)).ToArray();
        foreach (var sampleId in new[] { "1", "2" })
        {
            await sheets.AppendSampleAsync(
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

        var reportsRoot = Path.Combine(_directory, "Report");
        var export = new BendingReportExportService(sheets, reportsRoot);

        var chinese = await export.ExportAsync(sheetId, "zh-CN");
        var english = await export.ExportAsync(sheetId, "en-US");

        Assert.True(File.Exists(chinese));
        Assert.True(File.Exists(english));
        Assert.NotEqual(chinese, english);

        // 中文报表：抬头是中文，明细两行都按行展开
        var zh = new XlsFile();
        zh.Open(chinese);
        Assert.Equal("项目名称", zh.GetCellValue(3, 1));
        Assert.Equal("G15 高速 K12+300", zh.GetCellValue(3, 2));
        Assert.Equal("73.79", zh.GetCellValue(10, 2));
        Assert.Equal("1", zh.GetCellValue(14, 1));
        Assert.Equal("2", zh.GetCellValue(15, 1));
        Assert.Equal("73.47", zh.GetCellValue(14, 6));

        // 英文报表：同一份数据、英文抬头
        var en = new XlsFile();
        en.Open(english);
        Assert.Equal("Project", en.GetCellValue(3, 1));
        Assert.Equal("G15 高速 K12+300", en.GetCellValue(3, 2));
        Assert.Equal("2", en.GetCellValue(15, 1));
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
