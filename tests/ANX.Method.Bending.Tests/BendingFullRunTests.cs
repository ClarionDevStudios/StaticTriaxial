namespace ANX.Method.Bending.Tests;

using ANX.Communication.Channels;
using ANX.Control;
using ANX.Reporting;
using ANX.Storage;
using FlexCel.XlsAdapter;
using Microsoft.Data.Sqlite;

/// <summary>
/// **跑通一轮**：模拟器设备 → 阶段机 → 判定 → 落库 → 组统计 → 报表 + 曲线图。
/// 这是 M3 的验收形态（除界面外）：一条命令走完，产物是可打开的 Excel 与每试件一张 PNG。
/// 模拟器用 20 倍时间倍率，所以 8 秒的试验在这里约 0.4 秒跑完。
/// </summary>
public sealed class BendingFullRunTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-fullrun-{Guid.NewGuid():N}"
    );

    public BendingFullRunTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 模拟器应跑完一组并出报表与曲线图()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();
        var sheets = new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);

        await using var device = new SimulatedDeviceController(
            peakLoadKN: 9.0,
            displacementAtPeakMM: 5.0,
            sampleIntervalSeconds: 0.002,
            timeScale: 20
        );

        var runner = new BendingTestRunner(
            sheets,
            device,
            new TestRunProfile
            {
                ContactLoadKN = 0.05,
                BreakDropKN = 0.3,
                BreakConfirmSamples = 3,
                ResetSeconds = 0.5,
                MaxLoadKN = 50,
                MaxDisplacementMM = 20,
            }
        );

        await runner.EnsureConnectedAsync();
        var sheetId = await sheets.CreateAsync("T0715", "G15 高速 K12+300", "WT");

        var results = new List<BendingSampleResult>();
        foreach (var sampleId in new[] { "1", "2", "3" })
        {
            var result = await runner.RunSampleAsync(
                sheetId,
                new BendingSampleSpec(sampleId, SpanMM: 200, WidthMM: 30, HeightMM: 35),
                timeout: TimeSpan.FromSeconds(30)
            );

            Assert.NotNull(result);
            results.Add(result!);
        }

        var group = await runner.FinishGroupAsync(sheetId, results);
        Assert.Equal(3, group.EffectiveCount);

        // 报表 + 曲线图
        var templatePath = Path.Combine(_directory, "template.xlsx");
        BuildTemplate(templatePath);

        var report = await new BendingReportBuilder(sheets, new FlexCelReportExporter())
            .BuildAsync(sheetId, templatePath, Path.Combine(_directory, "out"));

        Assert.True(File.Exists(report.ReportPath));
        Assert.Equal(3, report.CurveImagePaths.Count);

        var workbook = new XlsFile();
        workbook.Open(report.ReportPath);
        Assert.Equal("G15 高速 K12+300", workbook.GetCellValue(1, 2));
        Assert.Equal("3", workbook.GetCellValue(2, 2));
        Assert.Equal("1", workbook.GetCellValue(3, 2));
        Assert.Equal("3", workbook.GetCellValue(5, 2)); // 明细第三行

        // 库里也留了三条明细
        var summaries = await sheets.QueryAsync("T0715");
        Assert.Single(summaries);
        Assert.Equal(3, summaries[0].SampleCount);
    }

    private static void BuildTemplate(string path)
    {
        var template = new XlsFile();
        template.NewFile();
        template.SetCellValue(1, 1, "项目名称");
        template.SetCellValue(1, 2, "{{Sheet.ProjectName}}");
        template.SetCellValue(2, 1, "有效试件数");
        template.SetCellValue(2, 2, "{{Result.EffectiveCount}}");
        template.SetCellValue(3, 1, "试件号");
        template.SetCellValue(3, 2, "{{Detail.SampleID}}");
        template.Save(path);
    }
}
