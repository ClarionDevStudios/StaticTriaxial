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
        // 方法只声明**自己**的参数（T0715 四项）；通用参数在通用目录里（比照现役"超集窗体"）
        var method = parameters.Definitions.ToArray();
        Assert.Equal(4, method.Length);
        Assert.All(method, d => Assert.False(string.IsNullOrWhiteSpace(d.Description)));

        var catalog = MachineParameterCatalog.Definitions;

        // 通道设置页：轴向压力 / 轴向位移 / 水平压力 / 水平位移（用户 2026-10-12：加水平压力、去掉双传感器）
        var channels = catalog.Where(d => d.Scope == ParameterScope.Acquisition).ToArray();
        Assert.Equal(4, channels.Length);
        Assert.Contains(channels, d => d.Key == MachineParameterCatalog.HorizontalPressChannelKey);
        Assert.DoesNotContain(
            channels,
            d => d.Key.Contains("DualSensor", StringComparison.Ordinal)
        );

        // 通道选择用下拉列表（现役 RadioGroup → 我们 ComboBox）
        Assert.All(channels, d => Assert.Equal(ParameterKind.Choice, d.Kind));
        Assert.All(channels, d => Assert.NotEmpty(d.Options!));

        // 2026-10-12：参数表里的项**全部接线完毕**（最后一项「不判破坏荷载下限」也接上了）。
        // PendingIntegration 这个闸门保留给以后新出现的"现役有、本软件还没功能"的项。
        Assert.DoesNotContain(catalog, d => d.PendingIntegration);
        Assert.Contains(
            catalog,
            d => d.Key == MachineParameterCatalog.PressChannelKey && !d.PendingIntegration
        );
        // 破坏后继续加载已接线（用户 2026-10-12 要求完成这一阶段），单位是**秒**
        var continueLoading = catalog.First(d =>
            d.Key == MachineParameterCatalog.ContinueLoadingTimeKey
        );
        Assert.False(continueLoading.PendingIntegration);
        Assert.Equal("s", continueLoading.Unit);
        Assert.All(catalog, d => Assert.False(string.IsNullOrWhiteSpace(d.Description)));

        // 用户 2026-10-12 指定的运行期显示顺序 + 已删除的三类项
        Assert.Equal(
            [
                MachineParameterCatalog.JogSpeedKey,
                "LoadRate",
                MachineParameterCatalog.DisplacementSafetyFactorKey,
                MachineParameterCatalog.LoadSafetyFactorKey,
                "ContactLoad",
                MachineParameterCatalog.MinBreakLoadKey,
                "BreakDrop",
                "BreakConfirmSamples",
                MachineParameterCatalog.ContinueLoadingTimeKey,
                MachineParameterCatalog.AutoFallTimeKey,
            ],
            MachineParameterCatalog.RuntimeOrderKeys
        );

        // 用户 2026-10-12 更名 + 移到运行期最后一行：原「破坏后回缩时间」→「试验完成后归位时间」，
        // 生效时点在「破坏后继续加载时间」之后，所以它必须是运行期最后一项。
        Assert.Equal(
            "试验完成后归位时间",
            catalog.First(d => d.Key == MachineParameterCatalog.AutoFallTimeKey).DisplayName
        );
        Assert.Equal(
            MachineParameterCatalog.AutoFallTimeKey,
            MachineParameterCatalog.RuntimeOrderKeys[^1]
        );
        Assert.Equal(
            MachineParameterCatalog.ContinueLoadingTimeKey,
            MachineParameterCatalog.RuntimeOrderKeys[^2]
        );
        Assert.DoesNotContain(
            catalog,
            d => d.Key.Contains("ReverseForce", StringComparison.Ordinal)
                || d.Key.Contains("HostJudges", StringComparison.Ordinal)
                || d.Key.Contains("DataRecording", StringComparison.Ordinal)
        );
        Assert.Empty(catalog.Where(d => d.Scope == ParameterScope.Presentation));

        // 位移通道下拉要含"位移通道1~4 / 千分表1~4 / 两组平均值"（值 = 现役的 0 基编号）
        var displace = catalog.First(d => d.Key == MachineParameterCatalog.DisplaceChannelKey);
        Assert.Equal(12, displace.Options!.Count);
        Assert.Equal("位移通道1", displace.Options![0].Label);
        Assert.Equal("千分表1", displace.Options![4].Label);
        Assert.Equal("位移1+2通道平均值", displace.Options![8].Label);
        Assert.Equal("千分表3+4通道平均值", displace.Options![11].Label);

        var press = catalog.First(d => d.Key == MachineParameterCatalog.PressChannelKey);
        Assert.Equal(["压力通道1", "压力通道2", "压力通道3", "压力通道4"], press.Options!.Select(o => o.Label));
        Assert.DoesNotContain(
            parameters.Definitions,
            d => d.Key is BendingSampleFields.SpanLengthKey
                or BendingSampleFields.SampleWidthKey
                or BendingSampleFields.SampleHeightKey
        );

        var values = await parameters.LoadAsync();
        Assert.Equal("50", values[BendingParameters.LoadRateKey]);
        // 通用参数（闸门系数等）不在方法声明里，但取值同样走 store（没存过就回落到目录默认值）
        Assert.Equal(
            "0.95",
            MachineParameterCatalog
                .Definitions.First(d => d.Key == MachineParameterCatalog.LoadSafetyFactorKey)
                .DefaultValue
        );

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

        // 把这一件的试样信息改 h=40 后重算：同一条曲线应算成 48.75（明细更新）
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

        // 单里只有 1 件 → **不足 3 件不写组结果**（用户 2026-10-13 按规范 5.6 定的口径）：
        // 均值列清空、件数与"需补做"标志照写
        var main = sheets.ReadMainRow(
            sheetId,
            ["FlexuralStrengthAvg", "EffectiveCount", "NeedsSupplement"]
        );
        Assert.Null(main["FlexuralStrengthAvg"]);
        Assert.Equal(1, Convert.ToDouble(main["EffectiveCount"]));
        Assert.Equal(1, Convert.ToDouble(main["NeedsSupplement"]));

        // 曲线本身没被动过
        Assert.Equal(curve.Length, sheets.ReadCurve(sheetId, "1").Count);
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
        var definition = MachineParameterCatalog.Definitions.First(d =>
            string.Equals(
                d.Key,
                MachineParameterCatalog.DisplacementSafetyFactorKey,
                StringComparison.Ordinal
            )
        );
        Assert.Equal("0.95", definition.DefaultValue);
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
        Assert.Equal("0.95", MachineParameterCatalog.Definitions.First(d =>
            string.Equals(
                d.Key,
                MachineParameterCatalog.LoadSafetyFactorKey,
                StringComparison.Ordinal
            )
        ).DefaultValue);
        Assert.Contains(BendingParameters.LoadSafetyFactorKey, BendingParameters.MachineKeys);
        Assert.Equal(0.95, adjusted.LoadRangeSafetyFactor, 6);
        Assert.Equal(95.0, adjusted.ClampLoadToDeviceRange(100.0).MaxLoadKN, 6);

        // 平台升降速率与试验速率是两个独立参数（用户 2026-10-12）：
        // 加载速率默认 50（试验用），平台升降默认 50（工作台按钮用），可分别改
        Assert.Equal("50", MachineParameterCatalog.Definitions.First(d =>
            string.Equals(
                d.Key,
                MachineParameterCatalog.JogSpeedKey,
                StringComparison.Ordinal
            )
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

        // 破坏后继续加载时间：参数表单位是**秒**（用户 2026-10-12 明确；现役线程也按秒比），
        // 值直接进 profile，不做 ms→s 换算
        Assert.Equal("5", MachineParameterCatalog.Definitions.First(d =>
            string.Equals(
                d.Key,
                MachineParameterCatalog.ContinueLoadingTimeKey,
                StringComparison.Ordinal
            )
        ).DefaultValue);
        Assert.Equal(5.0, split.ContinueLoadingSeconds, 6);

        // 不判破坏荷载下限：默认 0.2kN（现役弯曲线程常量 BREAK_MIN_LOAD_KN），直接进 profile
        Assert.Equal("0.2", MachineParameterCatalog.Definitions.First(d =>
            string.Equals(
                d.Key,
                MachineParameterCatalog.MinBreakLoadKey,
                StringComparison.Ordinal
            )
        ).DefaultValue);
        Assert.Equal(0.2, split.MinBreakLoadKN, 6);

        await parameters.SaveAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingParameters.ContinueLoadingTimeKey] = "10",
            }
        );
        Assert.Equal(10.0, (await execution.LoadProfileAsync()).ContinueLoadingSeconds, 6);

        // 原点修正开关：默认启用；改成 0 后判定不修正
        Assert.Equal("1", MachineParameterCatalog.Definitions.First(d =>
            string.Equals(
                d.Key,
                MachineParameterCatalog.EnableOriginCorrectionKey,
                StringComparison.Ordinal
            )
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
