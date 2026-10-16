namespace STS.Method.ShearStrength.Tests;

/// <summary>T 1236—2026 规范 4.6 停止判据的状态机用例（h0=306mm、Φ100 标准件）。</summary>
public sealed class ShearStrengthStopCriteriaTests
{
    private const double Height = 306;
    private const double Diameter = 100;

    [Fact]
    public void 持续上升应只在超过十五应变时停止()
    {
        var criteria = new ShearStrengthStopCriteria(Height, Diameter);
        var last = new ShearStopDecision(false, ShearStopReason.None);
        double stoppedAt = 0;

        // 每 mm 涨 0.5kN → 每 0.5mm 的轴向应力增量 ≈ 12~14kPa（> 5kPa，不触平台）
        for (var d = 0.5; d <= 46.5; d += 0.5)
        {
            last = criteria.Evaluate(d, 0.5 * d);

            if (last.ShouldStop)
            {
                stoppedAt = d;
                break;
            }
        }

        Assert.True(last.ShouldStop);
        Assert.Equal(ShearStopReason.StrainLimit, last.Reason);
        Assert.True(stoppedAt > 45.9, $"应在超过 15%（45.9mm）后才停，实际 {stoppedAt}mm");
    }

    [Fact]
    public void 荷载持平应判为平台停止()
    {
        var criteria = new ShearStrengthStopCriteria(Height, Diameter);

        for (var d = 0.5; d <= 10.0; d += 0.5)
        {
            Assert.False(criteria.Evaluate(d, 0.5 * d).ShouldStop);
        }

        // 10.0mm 之后荷载基本不再增加（5.00→5.02kN；校正面积微增，应力增量仅约 0.6kPa，小于 5kPa）
        var decision = criteria.Evaluate(10.5, 5.02);

        Assert.True(decision.ShouldStop);
        Assert.Equal(ShearStopReason.LoadPlateau, decision.Reason);
    }

    [Fact]
    public void 荷载小幅增量不足五应判平台()
    {
        var criteria = new ShearStrengthStopCriteria(Height, Diameter);

        for (var d = 0.5; d <= 10.0; d += 0.5)
        {
            Assert.False(criteria.Evaluate(d, 0.5 * d).ShouldStop);
        }

        // ΔP=0.03kN → Δσ≈1.5kPa < 5kPa
        var decision = criteria.Evaluate(10.5, 5.03);

        Assert.True(decision.ShouldStop);
        Assert.Equal(ShearStopReason.LoadPlateau, decision.Reason);
    }

    [Fact]
    public void 荷载回落应判为降低停止()
    {
        var criteria = new ShearStrengthStopCriteria(Height, Diameter);

        for (var d = 0.5; d <= 10.0; d += 0.5)
        {
            Assert.False(criteria.Evaluate(d, 0.5 * d).ShouldStop);
        }

        var decision = criteria.Evaluate(10.5, 4.8);

        Assert.True(decision.ShouldStop);
        Assert.Equal(ShearStopReason.LoadDropped, decision.Reason);
    }
}
