namespace ANX.Method.Bending.Tests;

using ANX.Control;
using ANX.Core;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 参数（试样尺寸 / 机器参数）存库并按分组给两个页面用；
/// 关键点：**改了试件高度，判定就必须按新高度算、并落进明细行**。
/// </summary>
public sealed class BendingParametersTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-param-{Guid.NewGuid():N}"
    );

    public BendingParametersTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 参数表不含试件尺寸且机器参数默认值可载入()
    {
        var parameters = CreateParameters();

        // 试件尺寸属于**试样信息**（用户 2026-10-12 明确），不再出现在方法参数里
        Assert.Empty(parameters.Definitions.Where(d => d.Group == ParameterGroup.Sample));
        Assert.Equal(8, parameters.Definitions.Count(d => d.Group == ParameterGroup.Machine));
        Assert.DoesNotContain(
            parameters.Definitions,
            d => d.Key is BendingSampleFields.SpanLengthKey
                or BendingSampleFields.SampleWidthKey
                or BendingSampleFields.SampleHeightKey
        );

        var values = await parameters.LoadAsync();
        Assert.Equal("50", values[BendingParameters.LoadRateKey]);
        Assert.Equal("0.95", values[BendingParameters.LoadSafetyFactorKey]);

        // 试样信息字段另有声明（跨径 / 宽 / 高）
        Assert.Equal(3, BendingSampleFields.Definitions.Count);
        Assert.Equal(
            "200",
            BendingSampleFields.Definitions.First(d => d.Key == BendingSampleFields.SpanLengthKey).DefaultValue
        );
    }

    [Fact]
    public async Task 存过的参数应覆盖默认值()
    {
        var parameters = CreateParameters();

        await parameters.SaveAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingParameters.BreakConfirmKey] = "5",
                [BendingParameters.LoadRateKey] = "55",
            }
        );

        var values = await parameters.LoadAsync();
        Assert.Equal("55", values[BendingParameters.LoadRateKey]);
        Assert.Equal("5", values[BendingParameters.BreakConfirmKey]);
        Assert.Equal("0.3", values[BendingParameters.BreakDropKey]); // 没存过的回落到默认
    }

    [Fact]
    public async Task 试件尺寸属于试样信息逐件生效并落库()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();
        var sheets = new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
        var parameters = new BendingParameters(new ParameterStore(initializer));
        var execution = new BendingExecution(sheets, parameters);

        var sheetId = await sheets.CreateAsync("T0715", "参数验证", "WT");
        var curve = Enumerable.Range(0, 40).Select(i => new CurvePoint(i * 0.1, i * 0.2)).ToArray();

        // 曲线峰值 7.8kN；没填过试样信息 → 用字段默认 h=35：R_B = 3·200·7800/(2·30·1225) = 63.67
        var before = await execution.CompleteSampleAsync(sheetId, "1", curve);
        Assert.NotNull(before);
        Assert.Equal(63.67, before!["flexuralStrength"], 0.01);

        // 第 2 件按**它自己**的试样信息填 h=40（尺寸逐件，不再动方法参数）
        await execution.SaveSampleInfoAsync(
            sheetId,
            "2",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingSampleFields.SampleHeightKey] = "40",
            }
        );

        var after = await execution.CompleteSampleAsync(sheetId, "2", curve);
        Assert.NotNull(after);
        Assert.Equal(48.75, after!["flexuralStrength"], 0.01); // 同一荷载、h=40 → 3·200·7800/(2·30·1600)

        // 明细行里记的也是新高度
        var rows = sheets.ReadDetailRows(sheetId, ["SampleID", "SampleHeight"]);
        Assert.Equal(2, rows.Count);
        Assert.Equal(40, Convert.ToDouble(rows[1]["SampleHeight"]));
    }

    /// <summary>
    /// **按当前参数重算历史曲线**（用户 2026-10-12 要求）：改了计算期参数（试件高度）后，
    /// 对已存曲线重算 → 明细结果与组统计按新参数更新；曲线本身不动。
    /// </summary>
    [Fact]
    public async Task 按当前参数重算应更新已存结果与组统计()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();
        var sheets = new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
        var parameters = new BendingParameters(new ParameterStore(initializer));
        var execution = new BendingExecution(sheets, parameters);

        var sheetId = await sheets.CreateAsync("T0715", "重算验证", "WT");
        var curve = Enumerable.Range(0, 40).Select(i => new CurvePoint(i * 0.1, i * 0.2)).ToArray();

        // 先按默认（h=35）结算一件：R_B = 63.67
        var before = await execution.CompleteSampleAsync(sheetId, "1", curve);
        Assert.Equal(63.67, before!["flexuralStrength"], 0.01);

        // 把这一件的试样信息改 h=40 后重算：同一条曲线应算成 48.75，明细与组统计都更新
        await execution.SaveSampleInfoAsync(
            sheetId,
            "1",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingSampleFields.SampleHeightKey] = "40",
            }
        );

        var report = await execution.RecomputeSheetAsync(sheetId);
        Assert.Equal(1, report.Recomputed);
        Assert.Equal(0, report.Skipped);

        var rows = sheets.ReadDetailRows(sheetId, ["FlexuralStrength", "SampleHeight"]);
        Assert.Equal(48.75, Convert.ToDouble(rows[0]["FlexuralStrength"]), 0.01);
        Assert.Equal(40, Convert.ToDouble(rows[0]["SampleHeight"]));

        var main = sheets.ReadMainRow(sheetId, ["FlexuralStrengthAvg", "EffectiveCount"]);
        Assert.Equal(48.75, Convert.ToDouble(main["FlexuralStrengthAvg"]), 0.01);
        Assert.Equal(1, Convert.ToDouble(main["EffectiveCount"]));

        // 曲线本身没被动过
        Assert.Equal(curve.Length, sheets.ReadCurve($"{sheetId}-1").Count);
    }

    /// <summary>
    /// 位移闸门安全系数进了参数表（用户 2026-10-12 要求现场可调）：默认 0.95、
    /// 归在机器参数组（参数配置页可见）、能装配进运行参数并驱动位移闸门。
    /// </summary>
    [Fact]
    public async Task 位移闸门安全系数应可从参数表调整()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();
        var parameters = new BendingParameters(new ParameterStore(initializer));
        var execution = new BendingExecution(
            new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]),
            parameters
        );

        // 默认 0.95，且归入机器参数（参数配置页）
        var definition = parameters.Definitions.First(d =>
            string.Equals(d.Key, BendingParameters.DisplacementSafetyFactorKey, StringComparison.Ordinal)
        );
        Assert.Equal("0.95", definition.DefaultValue);
        Assert.Contains(BendingParameters.DisplacementSafetyFactorKey, BendingParameters.MachineKeys);
        Assert.Contains(BendingParameters.DisplacementSafetyFactorKey, BendingParameters.MachineKeys);

        var byDefault = await execution.LoadProfileAsync();
        Assert.Equal(0.95, byDefault.DisplacementRangeSafetyFactor, 6);

        // 现场改成 0.8 → 装配出来的运行参数跟着变，25mm 量程算出 20mm 闸门
        await parameters.SaveAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingParameters.DisplacementSafetyFactorKey] = "0.8",
            }
        );

        var adjusted = await execution.LoadProfileAsync();
        Assert.Equal(0.8, adjusted.DisplacementRangeSafetyFactor, 6);
        Assert.Equal(20.0, adjusted.ClampDisplacementToDeviceRange(25.0).MaxDisplacementMM, 6);

        // 荷载闸门同样从参数表装配：默认 0.95（100kN → 95kN）
        Assert.Equal("0.95", parameters.Definitions.First(d =>
            string.Equals(d.Key, BendingParameters.LoadSafetyFactorKey, StringComparison.Ordinal)
        ).DefaultValue);
        Assert.Contains(BendingParameters.LoadSafetyFactorKey, BendingParameters.MachineKeys);
        Assert.Equal(0.95, adjusted.LoadRangeSafetyFactor, 6);
        Assert.Equal(95.0, adjusted.ClampLoadToDeviceRange(100.0).MaxLoadKN, 6);

        // 平台升降速率与试验速率是两个独立参数（用户 2026-10-12）：
        // 加载速率默认 50（试验用），平台升降默认 50（工作台按钮用），可分别改
        Assert.Equal("50", parameters.Definitions.First(d =>
            string.Equals(d.Key, BendingParameters.JogSpeedKey, StringComparison.Ordinal)
        ).DefaultValue);
        Assert.Equal(50.0, adjusted.JogSpeedMillimetersPerMinute, 6);

        await parameters.SaveAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingParameters.LoadRateKey] = "50",
                [BendingParameters.JogSpeedKey] = "20",
            }
        );

        var split = await execution.LoadProfileAsync();
        Assert.Equal(50.0, split.LoadRateMillimetersPerMinute, 6); // 试验过程速率
        Assert.Equal(20.0, split.JogSpeedMillimetersPerMinute, 6); // 平台升降速率

        // 原点修正开关：默认启用；改成 0 后判定不修正
        Assert.Equal("1", parameters.Definitions.First(d =>
            string.Equals(d.Key, BendingParameters.EnableOriginCorrectionKey, StringComparison.Ordinal)
        ).DefaultValue);
        await parameters.SaveAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingParameters.EnableOriginCorrectionKey] = "0",
            }
        );
    }

    private BendingParameters CreateParameters()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();

        return new BendingParameters(new ParameterStore(initializer));
    }
}
