namespace STS.Method.ShearStrength.Tests;

using System.Globalization;
using System.Text.Json;
using STS.Core;
using STS.Core.Communication;
using STS.Control;
using STS.Protocol.Geo;
using STS.Storage;

/// <summary>
/// **模拟链路全通验收**（用户 2026-10-16 的验收线："回环 / 模拟器即可，不需要真机"）：
/// 一套 9 件（3 级围压 × 每级 3 件）走完全链路 ——
/// 联机 → 阶段 A 接触（3~5kPa 即停）→ 阶段 B 施加围压（单发 C2 + 等稳压位）→
/// 阶段 C 剪切（位移调零 → 试验速率 → 判据停机）→ 卸围压 → 判定落库 → 整单收尾（c、φ、r）→ 出报表。
///
/// 驱动方式与执行页同构：真 `GeoDeviceController`（协议编解码）+ `GeoSimulator`（模拟下位机）+
/// `TestSequencer` + `ShearStrengthExecution`（真库）。**不依赖界面与真机**，全程确定性。
/// </summary>
public sealed class SimulatedFullRunTests : IDisposable
{
    private readonly TempSheetDatabase _database = new();
    private readonly LoopbackChannel _channel = new(new GeoSimulator());
    private readonly GeoDeviceController _controller;

    public SimulatedFullRunTests() =>
        _controller = new GeoDeviceController(
            _channel,
            commandTimeout: TimeSpan.FromMilliseconds(300),
            reversalPause: TimeSpan.Zero
        );

    public void Dispose()
    {
        _controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _database.Dispose();
    }

    [Fact]
    public async Task 模拟链路应跑通一套九件并收尾出报表()
    {
        var service = _database.CreateService();
        var execution = new ShearStrengthExecution(service);
        var sheetId = await execution.CreateSheetAsync("G15 高速 K12+300（模拟验收）", "WT");

        var controller = _controller; // 生命周期归 Dispose（避免测试内二次 Dispose）
        await controller.ConnectAsync();

        for (var i = 1; i <= 9; i++)
        {
            var sampleId = await execution.NextSampleIdAsync(sheetId); // S001…
            var sigma3 = i <= 3 ? 30.0 : i <= 6 ? 60.0 : 100.0;

            await execution.SaveSampleInfoAsync(
                sheetId,
                sampleId,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [ShearStrengthSampleFields.ConfiningPressureKey] = sigma3.ToString(
                        CultureInfo.InvariantCulture
                    ),
                    [ShearStrengthSampleFields.InitialHeightKey] = "306",
                    [ShearStrengthSampleFields.InitialDiameterKey] = "100",
                }
            );

            var profile = (await execution.LoadProfileAsync(new TestStartContext(sheetId, sampleId)))
                .ClampDisplacementToDeviceRange(controller.DisplacementRangeMillimeters)
                .ClampLoadToDeviceRange(controller.LoadRangeKN);

            var curve = await RunOneSpecimenAsync(controller, _channel, profile, sigma3, index: i);

            var values = await execution.CompleteSampleAsync(sheetId, sampleId, curve);
            Assert.NotNull(values);
            Assert.True(values!["shearStrength"] > sigma3, "σ1max 应大于围压 σ3");

            // 换件前把工作台退回起始位（对应现场的人工归位；下一件从 0 行程重来）
            await ReturnCarriageAsync(controller, _channel);
        }

        // ── 收尾：分级均值 → c、φ、r → CompletedOn ──
        var summary = await execution.CompleteSheetAsync(sheetId);

        // 结论行（界面弹框显示的就是这几行）：c、φ 两行必须在；"相关系数"那行在退化数据上不稳定，
        // 只断言它是那三种写法之一（可信 / 不可信 / 算不出）—— 真机数据下才有判别意义。
        Assert.Equal(ShearStrengthMessageKeys.CohesionLine, summary.Lines[0].ResourceKey);
        Assert.Equal(ShearStrengthMessageKeys.FrictionAngleLine, summary.Lines[1].ResourceKey);
        Assert.Contains(
            summary.Lines[2].ResourceKey,
            new[]
            {
                ShearStrengthMessageKeys.CorrelationReliable,
                ShearStrengthMessageKeys.CorrelationNotReliable,
                ShearStrengthMessageKeys.CorrelationNotComputable,
            }
        );

        var main = service.ReadMainRow(
            sheetId,
            ["LevelResultsJson", "CohesionKPa", "FrictionAngleDeg", "EnvelopeCorrelation", "EnvelopeReliable", "CompletedOn"]
        );

        var levels = JsonSerializer.Deserialize<List<LevelLine>>(
            Convert.ToString(main["LevelResultsJson"]) ?? "[]",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        )!;
        Assert.Equal(3, levels.Count);
        Assert.All(levels, level => Assert.Equal(3, level.Count));
        Assert.True(
            levels[0].MeanShearStrength < levels[1].MeanShearStrength
                && levels[1].MeanShearStrength < levels[2].MeanShearStrength,
            "各级平均剪切强度应随围压递增（σ1max = Pmax/A + σ3）"
        );

        // 模拟曲线每件形状相同（σ1max−σ3 近乎常数）→ 包络线**近乎退化**：c 仍能解出来、
        // φ 接近 0°（允许小噪声）。相关系数 / 可信标志在这类退化数据上不稳定，不作断言
        // —— 真机数据下才有判别意义（这里验的是"收尾链路跑得通、产物写得进"）。
        Assert.NotNull(main["CohesionKPa"]);
        Assert.True(
            Math.Abs(Convert.ToDouble(main["FrictionAngleDeg"])) < 1.0,
            "近乎退化的模拟数据：φ 应接近 0°"
        );
        Assert.False(string.IsNullOrWhiteSpace(Convert.ToString(main["CompletedOn"])));

        // ── 落库曲线按 0.5mm 抽样（接触后有效行程约 13.5mm → 约 28 点；原始帧 170+ 点）──
        var storedCurve = service.ReadCurve(sheetId, "S001");
        Assert.InRange(storedCurve.Count, 15, 60);
        Assert.Equal(0.5, storedCurve[1].X - storedCurve[0].X, 0.05); // 相邻抽样点间隔 0.5mm

        // ── 报告：整单导出（中英各一份）──
        var exportRoot = Path.Combine(
            Path.GetTempPath(),
            $"sts-accept-{Guid.NewGuid():N}"
        );
        try
        {
            var exportService = new ShearStrengthReportExportService(
                service,
                exportRoot,
                texts: null!,
                parameters: new ShearStrengthParameters()
            );

            var zhPath = await exportService.ExportAsync(sheetId, "zh-CN");
            Assert.True(File.Exists(zhPath));
            Assert.True(
                File.ReadAllBytes(zhPath).Length > 10_000,
                "报表应是一份有内容的文件（含明细与内嵌曲线图）"
            );
        }
        finally
        {
            if (Directory.Exists(exportRoot))
            {
                Directory.Delete(exportRoot, recursive: true);
            }
        }
    }

    /// <summary>
    /// 一件试件的三段式流程（与执行页同构）：
    /// A 接触段（3~5kPa 即停）→ B 施加围压并等稳压位 → C 位移调零 + 试验速率 + 判据停机 → 卸围压。
    /// </summary>
    private static async Task<IReadOnlyList<CurvePoint>> RunOneSpecimenAsync(
        GeoDeviceController controller,
        LoopbackChannel channel,
        TestRunProfile profile,
        double sigma3,
        int index
    )
    {
        TestSample latest = default;
        void OnSample(object? sender, TestSample sample) => latest = sample;

        controller.SampleReceived += OnSample;
        try
        {
            // ── 阶段 A：接触段（规范 4.3）──
            controller.ElapsedSeconds = 0;
            await controller.ZeroLoadAsync(); // 荷载调零（软件零点）
            await controller.SetSpeedAsync(profile.ApproachSpeedMillimetersPerMinute);

            var contactSequence = new TestSequencer(profile);
            contactSequence.Start();
            await controller.StartAsync();

            var guard = 0;
            var trace = new List<string>();
            while (contactSequence.Phase != TestPhase.Loading && guard++ < 200)
            {
                channel.Pump(1.0);
                controller.ElapsedSeconds += 1.0;
                _ = contactSequence.OnSample(latest with { ElapsedSeconds = controller.ElapsedSeconds });
                if (trace.Count < 8)
                {
                    trace.Add($"{guard}:{latest.Load:0.###}/{latest.RawDisplacement:0.###}");
                }
            }

            Assert.True(
                contactSequence.Phase == TestPhase.Loading,
                $"3~5kPa 正接触应成立；实际 {contactSequence.Phase}/{contactSequence.StopReason}"
                    + $"；第 {index} 件"
                    + $"；latest load={latest.Load:0.####} raw={latest.RawDisplacement:0.###} net={latest.Displacement:0.###}"
                    + $"；pumps={guard}"
                    + $"；前 8 拍 {string.Join(" | ", trace)}"
            );
            await controller.StopAsync();

            // ── 阶段 B：施加围压（单发 C2，等稳压位；稳压由"下位机"完成）──
            await controller.ApplyConfiningPressureAsync(sigma3);

            guard = 0;
            while (latest.ConfiningStable != true && guard++ < 200)
            {
                channel.Pump(0.1); // 缓升 100kPa/s：小步走到位（大 tick 会冲过目标）
            }

            Assert.True(latest.ConfiningStable == true, "围压应到达稳压位");
            Assert.Equal(sigma3, latest.ConfiningPressureKPa!.Value, 0.5);

            // ── 阶段 C：剪切段（位移调零 + 试验速率 + 判据停机）──
            var shearSequence = new TestSequencer(profile);
            shearSequence.Start();
            await controller.StartAsync(); // 启动序：C3('T',1) → C1（与执行页同构）

            guard = 0;
            var sheared = false;
            while (!shearSequence.IsFinished && guard++ < 5000)
            {
                channel.Pump(1.0);
                controller.ElapsedSeconds += 1.0;
                var sample = latest with { ElapsedSeconds = controller.ElapsedSeconds };

                var phaseBefore = shearSequence.Phase;
                if (!shearSequence.OnSample(sample))
                {
                    sheared = true;
                    break;
                }

                if (phaseBefore == TestPhase.ContactSearch && shearSequence.Phase == TestPhase.Loading)
                {
                    // 接触判定第一帧即成立（试件已接触）：位移调零（规范 4.4 尾）+ 切试验速率
                    await controller.ZeroDisplacementAsync();
                    await controller.SetSpeedAsync(profile.LoadRateMillimetersPerMinute);
                    controller.ElapsedSeconds = 0;
                }
            }

            Assert.True(sheared || shearSequence.IsFinished, "剪切段应能结束（判据或量程保护）");
            Assert.True(shearSequence.CanSettle, $"本轮应可结算，实际 {shearSequence.StopReason}");

            // 停止序（C1 停 → C3 停）+ 卸围压（C2 全 0）
            await controller.StopAsync();
            await controller.ReleaseConfiningPressureAsync();

            return shearSequence.Curve;
        }
        finally
        {
            controller.SampleReceived -= OnSample;
        }
    }

    /// <summary>把工作台退回起始位（现场人工归位；模拟器里就是反转把行程退回来）。</summary>
    private static async Task ReturnCarriageAsync(GeoDeviceController controller, LoopbackChannel channel)
    {
        TestSample latest = default;

        void OnSample(object? sender, TestSample sample) => latest = sample;

        controller.SampleReceived += OnSample;
        try
        {
            await controller.ReturnToStartAsync(50.0);

            var guard = 0;
            // 先泵一拍再判断：`latest` 初值是 default（原始位移 0），拿它做首判会"假装已经到 0"、
            // 一次都不泵（踩过：于是第 2 件从上一件的 23.77mm 处开始，直接撞位移闸门）。
            do
            {
                channel.Pump(1.0);
                guard++;
            }
            while (latest.RawDisplacement > 0.5 && guard < 100);
        }
        finally
        {
            controller.SampleReceived -= OnSample;
        }

        await controller.StopAsync();
    }

    /// <summary>回环通道：发送 → 喂模拟下位机 → 应答经 DataReceived 回来；<c>Pump</c> 人为推进仿真时间。</summary>
    private sealed class LoopbackChannel(GeoSimulator simulator) : ICommChannel
    {
        public bool IsOpen { get; private set; }

        public ChannelKind Kind => ChannelKind.Simulator;

        public string DisplayName => "回环（验收）";

        public event EventHandler<ReceivedChunk>? DataReceived;

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default
        )
        {
            Raise(simulator.Feed(data.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        /// <summary>人为推进一拍摄样（返回的 A1 原样从 DataReceived 送出）。</summary>
        public void Pump(double seconds) => Raise(simulator.Tick(seconds));

        private void Raise(byte[] bytes)
        {
            if (bytes.Length > 0)
            {
                DataReceived?.Invoke(this, new ReceivedChunk(bytes));
            }
        }
    }

    private sealed record LevelLine(double Sigma3, double MeanShearStrength, int Count);
}
