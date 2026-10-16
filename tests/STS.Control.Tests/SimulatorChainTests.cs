namespace STS.Control.Tests;

using STS.Communication.Channels;
using STS.Core;

/// <summary>
/// 模拟器 → 阶段机 的端到端：用模拟器的采样曲线（纯函数）喂阶段机，
/// 验证"一段完整的试验"能在没有真机、没有真实时间的情况下跑通并产生可结算的曲线。
/// </summary>
public sealed class SimulatorChainTests
{
    [Fact]
    public void 模拟器采样应驱动阶段机跑完一轮()
    {
        var sequencer = new STS.Control.TestSequencer(
            new STS.Control.TestRunProfile
            {
                ContactLoadKN = 0.05,
                BreakDropKN = 0.3,
                BreakConfirmSamples = 3,
                ResetSeconds = 0.5,
                MaxLoadKN = 50,
                MaxDisplacementMM = 20,
            }
        );
        sequencer.Start();

        var seconds = 0.0;
        var finished = false;

        // 每 20ms 喂一帧，最多 30 秒
        while (seconds <= 30.0 && !finished)
        {
            // contactGap 给 0：这一段直接把采样喂进阶段机（不经控制器），位移就从 0 起
            var sample = SampleCurve.At(
                seconds,
                peakLoadKN: 9.0,
                deflectionAtPeakMM: 5.0,
                contactGapMM: 0
            );
            finished = !sequencer.OnSample(sample);
            seconds += 0.02;
        }

        Assert.True(finished, "模拟器采样应能把试验跑完");
        Assert.Equal(TestStopReason.Completed, sequencer.StopReason);
        Assert.True(sequencer.CanSettle);
        Assert.True(sequencer.Curve.Count > 100, $"曲线点太少：{sequencer.Curve.Count}");
        Assert.Equal(9.0, sequencer.Peak.Load, 1e-9);
        Assert.Equal(5.0, sequencer.Peak.Displacement, 1e-9);
    }
}
