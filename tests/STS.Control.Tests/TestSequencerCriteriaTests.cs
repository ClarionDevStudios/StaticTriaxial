namespace STS.Control.Tests;

using STS.Core;
using STS.Control;

/// <summary>
/// 方法级停止判据接缝：判据说"停"之后的收尾按 profile 时间参数走
/// （继续加载 / 归位 / 直接完成）。判据缺失时走内建口径——旧用例在 TestSequencerTests 里守着。
/// </summary>
public sealed class TestSequencerCriteriaTests
{
    private sealed class ScriptedCriteria(double stopAtDisplacementMM) : ITestStopCriteria
    {
        public int Calls { get; private set; }

        public TestStopDecision Evaluate(
            double displacementMM,
            double loadKN,
            double elapsedSeconds
        )
        {
            Calls++;

            return displacementMM >= stopAtDisplacementMM
                ? new TestStopDecision(true, "脚本：到位停")
                : new TestStopDecision(false);
        }
    }

    private static TestRunProfile Profile(
        Func<ITestStopCriteria> factory,
        double resetSeconds = 0,
        double continueLoadingSeconds = 0
    ) =>
        new()
        {
            ContactLoadKN = 0.5,
            ContactTimeoutSeconds = 10,
            LoadingTimeoutSeconds = 60,
            StopCriteriaFactory = factory,
            ResetSeconds = resetSeconds,
            ContinueLoadingSeconds = continueLoadingSeconds,
        };

    [Fact]
    public void 判据说停且无收尾配置应直接完成()
    {
        var criteria = new ScriptedCriteria(stopAtDisplacementMM: 1.0);
        var sequencer = new TestSequencer(Profile(() => criteria));
        sequencer.Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        sequencer.OnSample(new TestSample(0.6, 0.5, 0.1)); // 接触

        Assert.True(sequencer.OnSample(new TestSample(5.0, 0.9, 0.2))); // 未到停点
        Assert.Equal(1, criteria.Calls); // 加载段每拍询问一次

        Assert.False(sequencer.OnSample(new TestSample(5.1, 1.0, 0.3))); // 到位 → 直接完成
        Assert.Equal(2, criteria.Calls);

        Assert.Equal(TestPhase.Completed, sequencer.Phase);
        Assert.Equal(TestStopReason.Completed, sequencer.StopReason);
        Assert.True(sequencer.CanSettle);
        Assert.Equal(3, sequencer.Curve.Count); // 接触帧 + 两帧加载
    }

    [Fact]
    public void 判据说停且配了归位应走归位后完成_归位段不记曲线()
    {
        var criteria = new ScriptedCriteria(stopAtDisplacementMM: 1.0);
        var sequencer = new TestSequencer(Profile(() => criteria, resetSeconds: 1.0));
        sequencer.Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        sequencer.OnSample(new TestSample(0.6, 0.5, 0.1)); // 接触
        Assert.True(sequencer.OnSample(new TestSample(5.0, 0.9, 0.2)));

        Assert.True(sequencer.OnSample(new TestSample(5.1, 1.0, 0.3))); // 说停 → 归位（仍在运行）
        Assert.Equal(TestPhase.Resetting, sequencer.Phase);

        var pointsAtReset = sequencer.Curve.Count;

        // 归位 1s：0.3 → 1.5 累计 1.2s → 完成；归位段曲线不增长
        Assert.False(sequencer.OnSample(new TestSample(5.1, 1.0, 1.5)));
        Assert.Equal(TestPhase.Completed, sequencer.Phase);
        Assert.Equal(TestStopReason.Completed, sequencer.StopReason);
        Assert.Equal(pointsAtReset, sequencer.Curve.Count);
        Assert.True(sequencer.CanSettle);
    }

    [Fact]
    public void 判据说停后继续加载段仍记曲线()
    {
        var criteria = new ScriptedCriteria(stopAtDisplacementMM: 1.0);
        var sequencer = new TestSequencer(
            Profile(() => criteria, resetSeconds: 1.0, continueLoadingSeconds: 0.25)
        );
        sequencer.Start();
        sequencer.OnSample(new TestSample(0, 0, 0));
        sequencer.OnSample(new TestSample(0.6, 0.5, 0.1)); // 接触
        Assert.True(sequencer.OnSample(new TestSample(5.0, 0.9, 0.2)));

        Assert.True(sequencer.OnSample(new TestSample(5.1, 1.0, 0.3))); // 说停 → 继续加载
        Assert.Equal(TestPhase.ContinueLoading, sequencer.Phase);
        Assert.Equal(3, sequencer.Curve.Count);

        // 继续加载段：曲线继续长（0.4 一拍 +1；0.8 累计 0.6s ≥ 0.25s → 切归位）
        Assert.True(sequencer.OnSample(new TestSample(5.1, 1.0, 0.4)));
        Assert.Equal(4, sequencer.Curve.Count);
        Assert.True(sequencer.OnSample(new TestSample(5.1, 1.0, 0.8)));
        Assert.Equal(TestPhase.Resetting, sequencer.Phase);
        Assert.Equal(5, sequencer.Curve.Count);

        // 归位段不记曲线；走满 1s 完成
        Assert.True(sequencer.OnSample(new TestSample(5.1, 1.0, 1.6)));
        Assert.False(sequencer.OnSample(new TestSample(5.1, 1.0, 2.2)));
        Assert.Equal(TestPhase.Completed, sequencer.Phase);
        Assert.Equal(5, sequencer.Curve.Count);
    }
}
