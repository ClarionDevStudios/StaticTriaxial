namespace ANX.Control.Tests;

using ANX.Core;
using ANX.Control;

/// <summary>
/// 阶段机与保护。全部用"喂采样"驱动，不依赖真实时间，所以结果确定、跑得快。
/// </summary>
public sealed class TestSequencerTests
{
    private static readonly TestRunProfile Profile = new()
    {
        ContactLoadKN = 0.5,
        BreakDropKN = 0.3,
        BreakConfirmSamples = 3,
        ContactTimeoutSeconds = 10,
        LoadingTimeoutSeconds = 60,
        ResetSeconds = 1.0,
    };

    /// <summary>
    /// 位移量程保护判的是**原始位移**（绝对值），不是接触调零后的净值 ——
    /// 保护对象是位移传感器的绝对位置，用净值判会漏掉"接触点已经靠近量程"的情形（现役同样判原始值）。
    /// </summary>
    [Fact]
    public void 位移量程保护应按原始位移判而非调零后的净值()
    {
        // 用真机那套闸门（设备位移量程 25mm × 0.95 = 23.75mm）
        var sequencer = new TestSequencer(Profile with { MaxDisplacementMM = 23.75 });
        sequencer.Start();
        sequencer.OnSample(new TestSample(0.6, 0, 0.1)); // 接触（净值已调零）

        // 净值还很小（0.5mm），但传感器绝对位置已经到 24mm（量程 23.75mm）→ 必须停机
        var stillRunning = sequencer.OnSample(
            new TestSample(9.0, 0.5, 0.2) { RawDisplacement = 24.0 }
        );

        Assert.False(stillRunning);
        Assert.Equal(TestStopReason.DisplacementRangeExceeded, sequencer.StopReason);
    }

    [Fact]
    public void 收到第一帧即进入接触搜索()
    {
        var sequencer = Start();

        Assert.Equal(TestPhase.Connecting, sequencer.Phase);
        Assert.True(sequencer.OnSample(new TestSample(0, 0, 0.02)));
        Assert.Equal(TestPhase.ContactSearch, sequencer.Phase);
    }

    [Fact]
    public void 接触后开始记录曲线并推进到加载()
    {
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        Assert.Empty(sequencer.Curve); // 未接触前不记点

        sequencer.OnSample(new TestSample(0.6, 0.5, 0.02));
        Assert.Equal(TestPhase.Loading, sequencer.Phase);
        Assert.Single(sequencer.Curve);
        Assert.Equal(new CurvePoint(0.5, 0.6), sequencer.Curve[0]);
    }

    /// <summary>
    /// 接触判据用**正向**比较（现役 `ActualLoad * 1000 >= FContactLoadKN * 1000`）：
    /// 传感器负向零漂（真机约 −0.19kN）不能算接触，否则一上电就"接触成立"、位移被提前清零、
    /// 挠度起算点错（用户 2026-10-12 真机发现）。
    /// </summary>
    [Fact]
    public void 负向零漂不应判为接触()
    {
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0, 0, 0)); // 收到第一帧 → 进入接触搜索

        // 阈值 0.5kN：−0.6kN 绝对值够大，但符号为负 → 必须仍是"未接触"
        sequencer.OnSample(new TestSample(-0.6, 0.2, 0.1));
        Assert.Equal(TestPhase.ContactSearch, sequencer.Phase);
        Assert.Empty(sequencer.Curve);

        // 正向 0.6kN 才成立
        sequencer.OnSample(new TestSample(0.6, 0.3, 0.2));
        Assert.Equal(TestPhase.Loading, sequencer.Phase);
        Assert.Single(sequencer.Curve);
    }

    [Fact]
    public void 荷载下降达阈值并连续确认后应进入复位并完成()
    {
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        sequencer.OnSample(new TestSample(0.6, 0.5, 0.1)); // 接触
        sequencer.OnSample(new TestSample(9.0, 5.0, 0.2)); // 峰值 9kN

        // 下降 0.35kN（≥0.3）→ 进入破坏确认
        sequencer.OnSample(new TestSample(8.65, 5.1, 0.3));
        Assert.Equal(TestPhase.Breaking, sequencer.Phase);

        sequencer.OnSample(new TestSample(8.60, 5.2, 0.4));
        sequencer.OnSample(new TestSample(8.55, 5.3, 0.5)); // 连续 3 帧 → 复位
        Assert.Equal(TestPhase.Resetting, sequencer.Phase);

        Assert.False(sequencer.OnSample(new TestSample(8.50, 5.4, 1.6))); // 复位满 1s → 完成
        Assert.Equal(TestPhase.Completed, sequencer.Phase);
        Assert.Equal(TestStopReason.Completed, sequencer.StopReason);
        Assert.True(sequencer.CanSettle);
        Assert.Equal(9.0, sequencer.Peak.Load, 1e-9);
    }

    /// <summary>
    /// 用户 2026-10-12：破坏确认后先「继续加载」一段，把破坏后的荷载下降趋势画进曲线，走完才归位；
    /// 归位段（电机反转送工作台回起始位）**不记曲线**。
    /// </summary>
    [Fact]
    public void 破坏后继续加载段记曲线而归位段不记曲线()
    {
        // 继续加载 0.25s：采样 0.1s 一拍，正好能看清"段内还在记点、走完才切归位"
        var sequencer = new TestSequencer(Profile with { ContinueLoadingSeconds = 0.25 });
        sequencer.Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        sequencer.OnSample(new TestSample(0.6, 0.5, 0.1)); // 接触
        sequencer.OnSample(new TestSample(9.0, 5.0, 0.2)); // 峰值 9kN
        sequencer.OnSample(new TestSample(8.65, 5.1, 0.3)); // 进入破坏确认
        sequencer.OnSample(new TestSample(8.60, 5.2, 0.4));
        sequencer.OnSample(new TestSample(8.55, 5.3, 0.5)); // 连续 3 帧 → 破坏确认

        Assert.Equal(TestPhase.ContinueLoading, sequencer.Phase);
        var pointsAtBreak = sequencer.Curve.Count;

        // 继续加载段：电机照旧前进，曲线继续长（图上"破坏后下降"的那一段就是它）
        sequencer.OnSample(new TestSample(8.50, 5.4, 0.6));
        Assert.Equal(TestPhase.ContinueLoading, sequencer.Phase);
        Assert.Equal(pointsAtBreak + 1, sequencer.Curve.Count);
        Assert.Equal(new CurvePoint(5.4, 8.50), sequencer.Curve[^1]);

        // 再走 0.2s（累计 0.3 ≥ 0.25）→ 进归位
        sequencer.OnSample(new TestSample(8.40, 5.5, 0.7));
        sequencer.OnSample(new TestSample(8.30, 5.6, 0.8));
        Assert.Equal(TestPhase.Resetting, sequencer.Phase);

        // 归位段：曲线不再增长
        var pointsAtReset = sequencer.Curve.Count;
        sequencer.OnSample(new TestSample(8.20, 5.7, 0.9));
        Assert.Equal(TestPhase.Resetting, sequencer.Phase);
        Assert.Equal(pointsAtReset, sequencer.Curve.Count);

        // 归位倒计时从进归位那一刻起算（0.8 → 2.0 = 1.2s ≥ 1.0s）→ 完成
        Assert.False(sequencer.OnSample(new TestSample(8.10, 5.8, 2.0)));
        Assert.Equal(TestPhase.Completed, sequencer.Phase);
        Assert.Equal(TestStopReason.Completed, sequencer.StopReason);
        Assert.True(sequencer.CanSettle);
        Assert.Equal(9.0, sequencer.Peak.Load, 1e-9);
        Assert.Equal(pointsAtReset, sequencer.Curve.Count);
    }

    /// <summary>继续加载时间为 0（没配）时走旧行为：破坏确认后直接归位。上面那条用例覆盖此路径。</summary>
    [Fact]
    public void 破坏确认期间又创新高应回到加载阶段()

    {
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        sequencer.OnSample(new TestSample(0.6, 0.5, 0.1));
        sequencer.OnSample(new TestSample(9.0, 5.0, 0.2));
        sequencer.OnSample(new TestSample(8.65, 5.1, 0.3)); // 进入 Breaking

        sequencer.OnSample(new TestSample(9.5, 5.2, 0.4)); // 创新高
        Assert.Equal(TestPhase.Loading, sequencer.Phase);
        Assert.Equal(9.5, sequencer.Peak.Load, 1e-9);
    }

    /// <summary>
    /// 「不判破坏荷载下限」（现役弯曲线程常量 BREAK_MIN_LOAD_KN = 0.2，判据 MaxLoad &gt;= 该值）：
    /// **峰值荷载**不到下限时，即使回落超过破坏判据也不判破坏。
    /// </summary>
    [Fact]
    public void 峰值低于不判破坏荷载下限时不判破坏()
    {
        // 下限 0.5kN，峰值只到 0.45kN：回落 0.35 ≥ 判据 0.3，但仍不该判破坏
        var belowFloor = new TestSequencer(
            Profile with
            {
                ContactLoadKN = 0.05,
                MinBreakLoadKN = 0.5,
            }
        );
        belowFloor.Start();
        belowFloor.OnSample(new TestSample(0, 0, 0));
        belowFloor.OnSample(new TestSample(0.06, 0.05, 0.1)); // 接触
        belowFloor.OnSample(new TestSample(0.45, 0.10, 0.2)); // 峰值 0.45kN
        belowFloor.OnSample(new TestSample(0.10, 0.15, 0.3)); // 回落 0.35kN
        Assert.Equal(TestPhase.Loading, belowFloor.Phase);

        // 同一段曲线，把下限降到 0.4kN 就该判破坏 —— 说明是"下限"在起作用，不是判据
        var aboveFloor = new TestSequencer(
            Profile with
            {
                ContactLoadKN = 0.05,
                MinBreakLoadKN = 0.4,
            }
        );
        aboveFloor.Start();
        aboveFloor.OnSample(new TestSample(0, 0, 0));
        aboveFloor.OnSample(new TestSample(0.06, 0.05, 0.1));
        aboveFloor.OnSample(new TestSample(0.45, 0.10, 0.2));
        aboveFloor.OnSample(new TestSample(0.10, 0.15, 0.3));
        Assert.Equal(TestPhase.Breaking, aboveFloor.Phase);

        // 下限 0（默认没配）＝ 不启用下限，旧行为不变
        Assert.Equal(0.0, new TestRunProfile().MinBreakLoadKN, 9);
    }

    [Theory]
    [InlineData(60.0, 1.0, TestStopReason.LoadRangeExceeded)]
    [InlineData(1.0, 99.0, TestStopReason.DisplacementRangeExceeded)]
    public void 超量程应中止(double load, double displacement, TestStopReason expected)
    {
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0, 0, 0));

        Assert.False(sequencer.OnSample(new TestSample(load, displacement, 1.0)));
        Assert.Equal(expected, sequencer.StopReason);
        Assert.Equal(TestPhase.Aborted, sequencer.Phase);
        Assert.False(sequencer.CanSettle);
    }

    [Theory]
    [InlineData(60.0, 1.0, TestStopReason.LoadRangeExceeded)]
    [InlineData(1.0, 99.0, TestStopReason.DisplacementRangeExceeded)]
    public void 量程停机但已经记到曲线时应可结算(
        double load,
        double displacement,
        TestStopReason expected
    )
    {
        // 与现役语义一致：量程触发时若已捕获破坏点 → 按峰值结算；
        // 未捕获（无明显峰值）→ 也结束，由判定器取曲线最大荷载结算。两者都要求曲线非空。
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0.6, 0.5, 0.1)); // 接触 → 开始记曲线
        sequencer.OnSample(new TestSample(9.0, 2.0, 0.2));
        sequencer.OnSample(new TestSample(9.2, 2.5, 0.3));

        Assert.False(sequencer.OnSample(new TestSample(load, displacement, 1.0)));

        Assert.Equal(expected, sequencer.StopReason);
        Assert.True(sequencer.CanSettle);
        Assert.Equal(9.2, sequencer.Peak.Load, 1e-9);
    }

    [Fact]
    public void 接触阶段超时应中止()
    {
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0, 0, 0));

        Assert.False(sequencer.OnSample(new TestSample(0.1, 0.1, 11.0))); // 超过 10s 仍未接触
        Assert.Equal(TestStopReason.ContactTimeout, sequencer.StopReason);
    }

    [Fact]
    public void 人工停止后不可结算()
    {
        var sequencer = Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        sequencer.OnSample(new TestSample(9.0, 5.0, 1.0));

        sequencer.Stop();

        Assert.Equal(TestStopReason.ManualStop, sequencer.StopReason);
        Assert.False(sequencer.CanSettle);
        Assert.False(sequencer.OnSample(new TestSample(9.0, 5.0, 2.0)));
    }

    [Fact]
    public void 未开始就喂采样应报错()
    {
        var sequencer = new TestSequencer(Profile);

        Assert.Throws<InvalidOperationException>(() => sequencer.OnSample(new TestSample(0, 0, 0)));
    }

    private static TestSequencer Start()
    {
        var sequencer = new TestSequencer(Profile);
        sequencer.Start();
        return sequencer;
    }
}
