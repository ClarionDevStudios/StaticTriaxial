namespace STS.Method.ShearStrength.Tests;

using System.Globalization;
using STS.Core;
using STS.Storage;

/// <summary>
/// 判定 / 落库实装（2026-10-16）：单件判定落明细与卡片值、分级均值随件刷新、收尾拟包络线
/// （c、φ、r + 可信标志）与完成时间、收尾闸口（级数 / 件数）、按存数据重算、试验后字段标记。
/// </summary>
public sealed class ShearStrengthCompletionTests : IDisposable
{
    private readonly TempSheetDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 单件判定应落明细并返回卡片值()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        await execution.SaveSampleInfoAsync(
            sheetId,
            "S001",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ShearStrengthSampleFields.InitialHeightKey] = "306",
                [ShearStrengthSampleFields.InitialDiameterKey] = "100",
                [ShearStrengthSampleFields.ConfiningPressureKey] = "60",
            }
        );

        // 0.1mm 一帧的"原始帧曲线"（21 点）：落库要按 0.5mm 抽样成 5 点（采集口径，用户 2026-10-16 定）
        var curve = Enumerable
            .Range(0, 21)
            .Select(i => new CurvePoint(i * 0.1, i * 0.155))
            .ToArray();

        var values = await execution.CompleteSampleAsync(sheetId, "S001", curve);

        Assert.NotNull(values);
        var expected = ShearStrengthAnalyzer.AnalyzeSample(curve, 306, 100, 60);
        Assert.Equal(expected.PeakLoadKN, values!["load"], 1e-9);
        Assert.Equal(expected.PeakDeformationMM, values["displacement"], 1e-9);
        Assert.Equal(expected.ShearStrengthKPa, values["shearStrength"], 1e-9);
        Assert.Equal(expected.AxialStrainPercent, values["axialStrain"], 1e-9);

        var row = service.ReadDetailRow(
            sheetId,
            "S001",
            ["MaxAxialLoad", "ShearStrength", "AxialStrain", "TestStatus", "StoppedAt15PercentStrain"]
        );

        Assert.NotNull(row);
        Assert.Equal(expected.PeakLoadKN * 1000.0, Convert.ToDouble(row!["MaxAxialLoad"]), 1e-6);
        Assert.Equal(expected.ShearStrengthKPa, Convert.ToDouble(row["ShearStrength"]), 1e-9);
        Assert.Equal(1.0, Convert.ToDouble(row["TestStatus"]), 1e-9);
        Assert.Equal(0.0, Convert.ToDouble(row["StoppedAt15PercentStrain"]), 1e-9);

        // 列表行口径：完成 + 判定值齐
        var rows = await execution.ReadSampleRowsAsync(sheetId);
        var single = Assert.Single(rows);
        Assert.True(single.IsCompleted);
        Assert.True(single.HasResult);

        // 落库曲线 = 原始帧按 0.5mm 抽样的记录曲线（0 / 0.5 / 1.0 / 1.5 / 2.0）
        var stored = service.ReadCurve(sheetId, "S001");
        Assert.Equal(5, stored.Count);
        Assert.Equal(0.5, stored[1].X, 1e-9);
        Assert.Equal(2.0, stored[^1].X, 1e-9);
    }

    [Fact]
    public async Task 收尾应按分级均值拟包络线并写完成时间()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        // 3 级 × 3 件；级均值为 130 / 220 / 340 kPa。
        // 莫尔圆点 (p,q) = (80,50) / (140,80) / (220,120)，正好落在 q = 0.5p + 10 上：
        // m = 0.5 → φ = 30°、b = 10 → c = 10 / cos30°、r = 1。
        await SeedCompletedLevelsAsync(service, sheetId, (30, 130), (60, 220), (100, 340));

        var summary = await execution.CompleteSheetAsync(sheetId);

        var main = service.ReadMainRow(
            sheetId,
            [
                "CohesionKPa",
                "FrictionAngleDeg",
                "EnvelopeCorrelation",
                "EnvelopeReliable",
                "CompletedOn",
                "LevelResultsJson",
            ]
        );

        Assert.Equal(10.0 / Math.Cos(30 * Math.PI / 180.0), Convert.ToDouble(main["CohesionKPa"]), 1e-6);
        Assert.Equal(30.0, Convert.ToDouble(main["FrictionAngleDeg"]), 1e-6);
        Assert.Equal(1.0, Convert.ToDouble(main["EnvelopeCorrelation"]), 1e-9);
        Assert.Equal(1L, Convert.ToInt64(main["EnvelopeReliable"]));
        Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(main["CompletedOn"])));

        var json = Convert.ToString(main["LevelResultsJson"]) ?? string.Empty;
        Assert.Contains("\"sigma3\":30", json, StringComparison.Ordinal);
        Assert.Contains("\"meanShearStrength\":130", json, StringComparison.Ordinal);
        Assert.Contains("\"count\":3", json, StringComparison.Ordinal);

        // 结论行（界面弹框显示的就是这三行）：键在方法侧、数值按报表口径格式化
        Assert.Equal(3, summary.Lines.Count);
        Assert.Equal(ShearStrengthMessageKeys.CohesionLine, summary.Lines[0].ResourceKey);
        Assert.Equal(ShearStrengthMessageKeys.FrictionAngleLine, summary.Lines[1].ResourceKey);
        Assert.Equal(ShearStrengthMessageKeys.CorrelationReliable, summary.Lines[2].ResourceKey);
        Assert.Equal(11.547, Convert.ToDouble(summary.Lines[0].Args[0], CultureInfo.InvariantCulture), 3);
        Assert.Equal("30", summary.Lines[1].Args[0]);
        Assert.Equal("1", summary.Lines[2].Args[0]);
    }

    [Fact]
    public async Task 相关系数算不出时结论行应带键说明而不是渲染NaN()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        // 三级围压下 σ1max − σ3 完全相同（各级 q 一致）→ 相关系数分母为 0：r 算不出；
        // c、φ 仍可解 —— m = 0 → φ = 0°、c = b = 50 kPa。
        await SeedCompletedLevelsAsync(service, sheetId, (30, 130), (60, 160), (100, 200));

        var summary = await execution.CompleteSheetAsync(sheetId);

        Assert.Equal(3, summary.Lines.Count);
        Assert.Equal(ShearStrengthMessageKeys.CohesionLine, summary.Lines[0].ResourceKey);
        Assert.Equal("50", summary.Lines[0].Args[0]);
        Assert.Equal(ShearStrengthMessageKeys.FrictionAngleLine, summary.Lines[1].ResourceKey);
        Assert.Equal("0", summary.Lines[1].Args[0]);
        Assert.Equal(ShearStrengthMessageKeys.CorrelationNotComputable, summary.Lines[2].ResourceKey);

        // 库里不写 NaN：相关系数存 NULL、可信标志置 0
        var main = service.ReadMainRow(sheetId, ["EnvelopeCorrelation", "EnvelopeReliable"]);
        Assert.True(main["EnvelopeCorrelation"] is null or DBNull);
        Assert.Equal(0L, Convert.ToInt64(main["EnvelopeReliable"]));
    }

    [Fact]
    public async Task 收尾闸口应带键报错_级数不足与件数不足()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        // 只有 2 级 → 级数不足
        await SeedCompletedLevelsAsync(service, sheetId, (30, 130), (60, 220));
        var notEnoughLevels = await Assert.ThrowsAsync<LocalizedException>(() =>
            execution.CompleteSheetAsync(sheetId)
        );
        Assert.Equal(ShearStrengthMessageKeys.NeedMoreLevels, notEnoughLevels.Messages[0].ResourceKey);

        // 第 3 级只有 2 件 → 件数不足
        await service.AppendSampleAsync(sheetId, "S007", [], Completed(100, 340));
        await service.AppendSampleAsync(sheetId, "S008", [], Completed(100, 340));
        var incomplete = await Assert.ThrowsAsync<LocalizedException>(() =>
            execution.CompleteSheetAsync(sheetId)
        );
        Assert.Equal(ShearStrengthMessageKeys.LevelIncomplete, incomplete.Messages[0].ResourceKey);

        // 补第 3 件 → 通过（收尾成功）
        await service.AppendSampleAsync(sheetId, "S009", [], Completed(100, 340));
        await execution.CompleteSheetAsync(sheetId);
    }

    [Fact]
    public async Task 重算应按存数据重判并跳过判不了的件()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var sheetId = await execution.CreateSheetAsync("项目", "WT");

        await execution.SaveSampleInfoAsync(
            sheetId,
            "S001",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ShearStrengthSampleFields.InitialHeightKey] = "306",
                [ShearStrengthSampleFields.InitialDiameterKey] = "100",
                [ShearStrengthSampleFields.ConfiningPressureKey] = "60",
            }
        );

        var curve = new CurvePoint[] { new(0, 0), new(1, 1.5), new(2, 2.8), new(3, 3.6) };
        await service.UpdateDetailFieldsAsync(
            sheetId,
            "S001",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["ShearStrength"] = 999.0, // 先写一个错值，重算应改回来
                ["TestStatus"] = 1,
            },
            curve
        );

        // 一件没有曲线（判不了）→ 跳过
        await service.AppendSampleAsync(sheetId, "S002", [], Completed(60, 220));

        var report = await execution.RecomputeSheetAsync(sheetId);

        Assert.Equal(1, report.Recomputed);
        Assert.Equal(1, report.Skipped);

        var expected = ShearStrengthAnalyzer.AnalyzeSample(curve, 306, 100, 60);
        var row = service.ReadDetailRow(sheetId, "S001", ["ShearStrength"]);
        Assert.Equal(expected.ShearStrengthKPa, Convert.ToDouble(row!["ShearStrength"]), 1e-9);
    }

    [Fact]
    public void 试验后字段应带PostTest标记()
    {
        var postTest = ShearStrengthSampleFields
            .Definitions.Where(definition => definition.PostTest)
            .Select(definition => definition.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "DryDensity", "Mass", "WaterContent", "WaterContentLoss" }
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray(),
            postTest
        );
    }

    /// <summary>按"每级 3 件"造已完成明细（结果直接给级均值强度；曲线留空）。</summary>
    private static async Task SeedCompletedLevelsAsync(
        TestSheetService service,
        string sheetId,
        params (double Sigma3, double Strength)[] levels
    )
    {
        var index = 0;

        foreach (var (sigma3, strength) in levels)
        {
            for (var i = 0; i < 3; i++)
            {
                index++;
                await service.AppendSampleAsync(sheetId, $"S{index:D3}", [], Completed(sigma3, strength));
            }
        }
    }

    private static Dictionary<string, double> Completed(double sigma3, double strength) =>
        new(StringComparer.Ordinal)
        {
            ["ConfiningPressure"] = sigma3,
            ["ShearStrength"] = strength,
            ["TestStatus"] = 1,
        };
}
