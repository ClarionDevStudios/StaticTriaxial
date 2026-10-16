namespace ANX.Method.Marshall.Tests;

using ANX.Communication.Channels;
using ANX.Control;
using ANX.Core;
using ANX.Core.Control;
using ANX.Core.Reporting;
using ANX.Reporting;
using ANX.Storage;
using FlexCel.XlsAdapter;
using Microsoft.Data.Sqlite;

/// <summary>
/// **跑通一轮**（模拟器）：建单 → 逐件试验（阶段机出曲线）→ 判定落库 → 组统计 → 报表 + 曲线图。
/// 用的是与界面同一条链路（<see cref="MarshallExecution"/> 的两个契约），只是设备换成模拟器。
/// </summary>
public sealed class MarshallFullRunTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-t0709-fullrun-{Guid.NewGuid():N}"
    );

    public MarshallFullRunTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 模拟器跑完一组应落库判组统计并出报表与曲线图()
    {
        var module = new MarshallPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();
        var sheets = new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
        var execution = new MarshallExecution(
            sheets,
            new MarshallParameters(new ParameterStore(initializer))
        );

        await using var device = new SimulatedDeviceController(
            peakLoadKN: 9.0,
            deflectionAtPeakMM: 5.0,
            sampleIntervalSeconds: 0.002,
            timeScale: 20,
            loadNoiseRatio: 0 // 修正直线拟合对噪声敏感（与弯曲整轮用例同一条经验）
        );
        await device.ConnectAsync();

        // 建单：照现役混合料马歇尔（T0709-2025 / 标准马歇尔 / φ101.6×63.5）
        var sheetId = await execution.CreateSheetAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SheetID"] = "MSXX",
                ["ClientUnits"] = "某检测中心",
                ["ProjectName"] = "G15 高速 K12+300",
                ["TestSpecificationsID"] = "0",
                ["TestStandardsID"] = "0",
                ["SampleSizeID"] = "0",
                ["AsphaltMixtureType"] = "AC-13",
                ["TestTemperatureDesc"] = "60",
                ["AcceptanceDate"] = "2026-10-10",
                ["SampleDensity"] = "2.432",
                ["VoidRatio"] = "4.2",
                ["AsphaltUsage"] = "5.1",
                ["ContentByVolume"] = "11.4",
                ["AsphaltSaturation"] = "68.3",
                ["MineralAggregateVoidRatio"] = "15.1",
                ["Tester"] = "王工",
            }
        );

        Assert.StartsWith("MS", sheetId, StringComparison.Ordinal);

        // 单上的"选项 + 描述"成对落库（报表取的就是描述列）
        var main = sheets.ReadMainRow(
            sheetId,
            ["TestSpecificationsDesc", "TestStandardsDesc", "SampleSizeDesc", "CreatedOn"]
        );
        Assert.Equal(
            "T 0709-2025 沥青混合料马歇尔稳定度试验",
            Convert.ToString(main["TestSpecificationsDesc"])
        );
        Assert.Equal("标准马歇尔试验", Convert.ToString(main["TestStandardsDesc"]));
        Assert.Equal("Φ101.6 × 63.5mm", Convert.ToString(main["SampleSizeDesc"]));

        var profile = await execution.LoadProfileAsync();

        for (var index = 1; index <= 3; index++)
        {
            var sampleId = await execution.NextSampleIdAsync(sheetId);

            await execution.SaveSampleInfoAsync(
                sheetId,
                sampleId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["GroupID"] = "0",
                    ["SampleInitialDiameter"] = "101.6",
                    ["SampleInitialHeight"] = "63.5",
                    ["StrainRate"] = "50",
                }
            );

            var curve = await RunOnceAsync(device, profile);
            var metrics = await execution.CompleteSampleAsync(sheetId, sampleId, curve);

            Assert.NotNull(metrics);
            var stability = metrics!["stability"];
            var flow = metrics["flowValue"];

            // 模拟器的峰值就是 9.0kN、峰值变形 5.0mm：判定值应落在合理区间
            Assert.InRange(stability, 8.0, 10.0);
            Assert.InRange(flow, 0.1, 6.0);
        }

        await execution.CompleteSheetAsync(sheetId);

        // 组统计：三件同曲线 → 均值 ≈ 9、标准差 0；第二组没有 → 残留稳定度比为空
        var stats = sheets.ReadMainRow(
            sheetId,
            [
                "Group1AvgStability",
                "Group1Stability_Std_Dev",
                "Group1Stability_Cv_Percent",
                "Group1AvgFlowValue",
                "StabilityRatio",
            ]
        );
        var average = Convert.ToDouble(stats["Group1AvgStability"]);
        Assert.InRange(average, 8.0, 10.0);
        Assert.InRange(Convert.ToDouble(stats["Group1Stability_Std_Dev"]), 0.0, 0.01);
        Assert.NotNull(stats["Group1AvgFlowValue"]);
        Assert.True(stats["StabilityRatio"] is null or DBNull);

        // 试样列表：三件都完成、都有判定值
        var rows = await execution.ReadSampleRowsAsync(sheetId);
        Assert.Equal(3, rows.Count);
        Assert.Equal(["S001", "S002", "S003"], rows.Select(row => row.SampleId));
        Assert.All(rows, row => Assert.True(row.IsCompleted));
        Assert.All(rows, row => Assert.True(row.HasResult));

        // 报表 + 曲线图
        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var report = await new MarshallReportBuilder(sheets, new FlexCelReportExporter())
            .BuildAsync(
                sheetId,
                MarshallTemplates.Resolve(templatesRoot, "zh-CN"),
                Path.Combine(_directory, "Report"),
                "zh-CN",
                new MinimalTexts()
            );

        Assert.True(File.Exists(report.ReportPath));
        Assert.Equal(3, report.CurveImagePaths.Count);
        Assert.All(report.CurveImagePaths, path => Assert.True(File.Exists(path)));

        var workbook = new XlsFile();
        workbook.Open(report.ReportPath);
        Assert.Equal("G15 高速 K12+300", workbook.GetCellValue(2, 3));
        Assert.Equal("标准马歇尔试验", workbook.GetCellValue(3, 6));
        Assert.Equal("S001", workbook.GetCellValue(11, 1));
        Assert.Equal("S003", workbook.GetCellValue(13, 1));
        Assert.Equal("合格", workbook.GetCellValue(11, 8));

        // 库里也留了三条明细（按试件号可查）
        var summaries = await sheets.QueryAsync("T0709");
        Assert.Single(summaries);
        Assert.Equal(3, summaries[0].SampleCount);
    }

    /// <summary>用阶段机跑一件：启动设备 → 吃到结束 → 返回曲线（与 <c>BendingTestRunner</c> 同款）。</summary>
    private static async Task<IReadOnlyList<CurvePoint>> RunOnceAsync(
        ITestDeviceController device,
        TestRunProfile profile
    )
    {
        var sequencer = new TestSequencer(profile);
        var finished = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        void OnSample(object? sender, TestSample sample)
        {
            if (!sequencer.OnSample(sample))
            {
                finished.TrySetResult(true);
            }
        }

        sequencer.Start();
        device.SampleReceived += OnSample;

        try
        {
            await device.StartAsync();

            var completed = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            Assert.Same(finished.Task, completed);
        }
        finally
        {
            device.SampleReceived -= OnSample;
            await device.StopAsync();
        }

        return sequencer.Curve;
    }

    /// <summary>最小文案解析器（报表里"合格/剔除"与试验类型走资源键）。</summary>
    private sealed class MinimalTexts : ITextResolver
    {
        public string Get(string key, string culture) =>
            key switch
            {
                "Method.T0709.ValidityNormal" => "合格",
                "Method.T0709.ValidityAbnormal" => "剔除",
                "Method.T0709.TestTypeStandard" => "标准马歇尔试验",
                "Method.T0709.TestTypeImmersion" => "浸水马歇尔试验",
                "Method.T0709.TestTypeVacuum" => "真空饱水马歇尔试验",
                _ => key,
            };
    }
}
