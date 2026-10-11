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
