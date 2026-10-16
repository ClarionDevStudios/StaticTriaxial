namespace ANX.Method.Marshall.Tests;

using ANX.Core;

/// <summary>
/// 马歇尔稳定度判定（照现役 `Form\MarshallStabilityTest\MarshallAnalyzer.pas` 搬）。钉四件事：
/// 明显峰取最大荷载、不明显峰取 1.5mm 偏移线交点、修正直线拟合（10%~80% 区间 + 3σ 剔除）、
/// 以及"半界值向零截断"的 `SimpleRoundTo` 修约口径。
/// </summary>
public sealed class MarshallAnalyzerTests
{
    // ────────────────────────── 明显峰（6.1） ──────────────────────────

    /// <summary>
    /// 明显峰造点：y = 3(x − 1.5) 直线上升到 (4.5, 9.0)，随后自由下落。
    /// 修正直线可精确还原 y = 3x − 4.5（O′ = 1.5），强度/流值都是现场常见量级。
    /// </summary>
    private static CurvePoint[] ObviousCurve()
    {
        var points = new List<CurvePoint>();
        for (var i = 0; i <= 30; i++)
        {
            var x = 1.5 + (i * 0.1);
            points.Add(new CurvePoint(x, 3.0 * (x - 1.5)));
        }

        for (var i = 1; i <= 6; i++)
        {
            var x = 4.5 + (i * 0.1);
            points.Add(new CurvePoint(x, 9.0 - (2.0 * i * 0.1)));
        }

        return [.. points];
    }

    [Fact]
    public void 明显峰取最大荷载且流值扣修正原点()
    {
        var result = MarshallAnalyzer.CalcObviousPeak(ObviousCurve());

        Assert.True(result.Succeeded);
        Assert.True(result.IsObviousPeak);
        Assert.False(result.UsedFallbackLastPoint);

        // 修正直线 = 造点用的那条线：k = 3、b = −4.5、O′ = 1.5
        Assert.Equal(3.0, result.TangentK, 6);
        Assert.Equal(-4.5, result.TangentB, 6);
        Assert.Equal(1.5, result.OPrime, 9);

        // 峰值点就是造点的顶点
        Assert.Equal(4.5, result.PeakX, 9);
        Assert.Equal(9.0, result.PeakY, 9);
        Assert.Equal(9.0, result.MSRaw, 9);

        // 流值 = 峰值变形 − O′ = 4.5 − 1.5，两位小数
        Assert.Equal(3.0, result.FLValue, 9);
    }

    [Fact]
    public void 拟合区间参数非法时回退到默认10到80()
    {
        var curve = ObviousCurve();
        var byDefault = MarshallAnalyzer.CalcObviousPeak(curve);
        var byBadRange = MarshallAnalyzer.CalcObviousPeak(curve, 150.0, 50.0);

        Assert.Equal(byDefault.TangentK, byBadRange.TangentK);
        Assert.Equal(byDefault.TangentB, byBadRange.TangentB);
        Assert.Equal(byDefault.OPrime, byBadRange.OPrime);
        Assert.Equal(byDefault.FLValue, byBadRange.FLValue);
    }

    [Fact]
    public void 三点西格玛剔除单帧毛刺后重拟合()
    {
        // y = 2x（x = 0..40），只在 x = 10 处塞一个 +15kN 的单帧毛刺（2×10=20 → 35）
        var points = new List<CurvePoint>();
        for (var i = 0; i <= 40; i++)
        {
            points.Add(new CurvePoint(i, i == 10 ? 35.0 : 2.0 * i));
        }

        Assert.True(MarshallAnalyzer.TryFindTangentLine(points, out var k, out var b));

        // 毛刺被 3σ 剔除后，拟合还原出干净的 y = 2x
        Assert.Equal(2.0, k, 9);
        Assert.Equal(0.0, b, 9);

        var result = MarshallAnalyzer.CalcObviousPeak(points);
        Assert.Equal(40.0, result.PeakX, 9);
        Assert.Equal(80.0, result.MSRaw, 9);
    }

    [Fact]
    public void 拟合失败时回落全零结果且不抛异常()
    {
        // 点数不足 3：现役直接返回空记录
        var tooFew = MarshallAnalyzer.CalcObviousPeak([new CurvePoint(1, 1), new CurvePoint(2, 2)]);
        Assert.False(tooFew.Succeeded);
        Assert.True(tooFew.IsObviousPeak);
        Assert.Equal(0.0, tooFew.MSRaw, 9);
        Assert.Equal(0.0, tooFew.FLValue, 9);

        // 全 0 曲线：峰值非正，拟合失败
        var flat = new List<CurvePoint>();
        for (var i = 0; i <= 30; i++)
        {
            flat.Add(new CurvePoint(i * 0.1, 0.0));
        }

        Assert.False(MarshallAnalyzer.CalcNonObviousPeak(flat).Succeeded);

        // 全程下降：峰值在第一点，峰值之前的区间点不足 3 个，拟合失败
        var descending = new List<CurvePoint>();
        for (var i = 0; i <= 30; i++)
        {
            descending.Add(new CurvePoint(i * 0.1, 10.0 - (i * 0.1)));
        }

        var failed = MarshallAnalyzer.CalcObviousPeak(descending);
        Assert.False(failed.Succeeded);
        Assert.Equal(0.0, failed.TangentK, 9);
    }

    // ────────────────────────── 不明显峰（6.2） ──────────────────────────

    /// <summary>
    /// 不明显峰造点：y = 3(x − 1) 升到 (4, 9)，之后转入缓升 y = 9 + 0.5(x − 4)（永不下降，
    /// 对应现场"位移满量程停机、未出现峰值"的形态）。修正直线仍是 y = 3x − 3（O′ = 1）；
    /// 1.5mm 偏移线 y = 3x − 7.5 与曲线在 x = 5.8 相交（此时两线正好相等）。
    /// </summary>
    private static CurvePoint[] NonObviousCurve()
    {
        var points = new List<CurvePoint>();
        for (var i = 0; i <= 30; i++)
        {
            var x = 1.0 + (i * 0.1);
            points.Add(new CurvePoint(x, 3.0 * (x - 1.0)));
        }

        for (var i = 1; i <= 30; i++)
        {
            var x = 4.0 + (i * 0.1);
            points.Add(new CurvePoint(x, 9.0 + (0.5 * (x - 4.0))));
        }

        return [.. points];
    }

    [Fact]
    public void 不明显峰取偏移线交点()
    {
        var result = MarshallAnalyzer.CalcNonObviousPeak(NonObviousCurve());

        Assert.True(result.Succeeded);
        Assert.False(result.IsObviousPeak);
        Assert.False(result.UsedFallbackLastPoint);

        Assert.Equal(3.0, result.TangentK, 6);
        Assert.Equal(-3.0, result.TangentB, 6);
        Assert.Equal(1.0, result.OPrime, 9);

        // 交点：曲线 9 + 0.5(x − 4) 与偏移线 3x − 7.5 → x = 5.8、y = 9.9
        Assert.Equal(5.8, result.PeakX, 9);
        Assert.Equal(9.9, result.PeakY, 9);
        Assert.Equal(9.9, result.MSRaw, 9);

        // 流值 = 5.8 − 1.0 = 4.8 → 两位小数
        Assert.Equal(4.8, result.FLValue, 9);
    }

    [Fact]
    public void 无明显峰且未找到交点时兜底取末点()
    {
        // 斜率与修正直线一致、永不下降：偏移线始终在曲线下方 4.5kN，穿越不发生
        var points = new List<CurvePoint>();
        for (var i = 0; i <= 60; i++)
        {
            var x = 1.0 + (i * 0.1);
            points.Add(new CurvePoint(x, 3.0 * (x - 1.0)));
        }

        var result = MarshallAnalyzer.CalcNonObviousPeak(points);

        Assert.True(result.Succeeded);
        Assert.True(result.UsedFallbackLastPoint);
        Assert.Equal(7.0, result.PeakX, 9);
        Assert.Equal(18.0, result.MSRaw, 9);

        // 流值 = 7.0 − O′ = 7.0 − 1.0（修正直线 y = 3x − 3 的横轴交点）
        Assert.Equal(1.0, result.OPrime, 9);
        Assert.Equal(6.0, result.FLValue, 9);
    }

    // ────────────────────────── 落库前的换算 ──────────────────────────

    [Fact]
    public void 修正稳定度与马歇尔模量按现役口径修约()
    {
        // MS′ = MS × K，两位小数
        Assert.Equal(10.3, MarshallAnalyzer.CorrectedStability(9.9, 1.04), 9);
        Assert.Equal(10.12, MarshallAnalyzer.CorrectedStability(10.123, 1.0), 9);

        // T = MS / FL，两位小数（用户 2026-10-16 定的口径）
        Assert.Equal(3.54, MarshallAnalyzer.MarshallModulus(9.9, 2.8), 9);

        // 流值不大于 0.01 时不计算，记 0
        Assert.Equal(0.0, MarshallAnalyzer.MarshallModulus(10.0, 0.005), 9);
    }

    /// <summary>
    /// **流值取两位小数**（用户 2026-10-16 定的口径：取值 / 存储 / 显示三处一致）。
    /// 造一条峰值落点带第三位小数的曲线：修正直线仍是 y = 2x − 3（O′ = 1.5），
    /// 峰值在 x = 4.567 → 流值 = 4.567 − 1.5 = 3.067 → **3.07**（一位小数会得 3.1，本用例就是钉这个差别）。
    /// </summary>
    [Fact]
    public void 流值取两位小数()
    {
        var points = new List<CurvePoint>();
        for (var i = 0; i <= 30; i++) // x = 1.5..4.5，y = 2x − 3
        {
            var x = 1.5 + (i * 0.1);
            points.Add(new CurvePoint(x, (2.0 * x) - 3.0));
        }

        // 峰值点（y 最大）落在 x = 4.567：第三位小数就在这儿
        points.Add(new CurvePoint(4.567, (2.0 * 4.567) - 3.0));
        points.Add(new CurvePoint(4.6, 5.5)); // 之后下落

        var result = MarshallAnalyzer.CalcObviousPeak(points);

        Assert.True(result.Succeeded);
        Assert.Equal(1.5, result.OPrime, 9);
        Assert.Equal(4.57, result.PeakX, 9); // 峰值变形两位
        Assert.Equal(3.07, result.FLValue, 9); // 流值两位（一位会得 3.1）
        Assert.Equal(6.13, result.MSRaw, 9); // 峰值荷载两位
    }

    // ────────────────────────── 疑似无效判定 ──────────────────────────

    [Fact]
    public void 峰值落在最后三个点时判定疑似无效()
    {
        // 单调上升：峰值在末点
        var rising = new List<CurvePoint>();
        for (var i = 0; i <= 20; i++)
        {
            rising.Add(new CurvePoint(i * 0.1, i * 0.2));
        }

        Assert.True(MarshallAnalyzer.IsCurvePeakAtEnd(rising));

        // 峰值在倒数第 3 个点：仍算"未出现下降沿"
        Assert.True(
            MarshallAnalyzer.IsCurvePeakAtEnd(
                [
                    new CurvePoint(0, 1),
                    new CurvePoint(1, 2),
                    new CurvePoint(2, 5),
                    new CurvePoint(3, 4),
                    new CurvePoint(4, 3),
                ]
            )
        );

        // 峰值偏前：出现了明显的下降沿
        Assert.False(
            MarshallAnalyzer.IsCurvePeakAtEnd(
                [
                    new CurvePoint(0, 1),
                    new CurvePoint(1, 5),
                    new CurvePoint(2, 4),
                    new CurvePoint(3, 3),
                    new CurvePoint(4, 2),
                ]
            )
        );

        // 点太少：不判定
        Assert.False(MarshallAnalyzer.IsCurvePeakAtEnd([new CurvePoint(0, 1), new CurvePoint(1, 2)]));
    }

    // ────────────────────────── 修约口径 ──────────────────────────

    [Theory]
    [InlineData(1.234, -2, 1.23)]
    [InlineData(1.236, -2, 1.24)]
    [InlineData(-1.236, -2, -1.24)]
    [InlineData(2.06, -1, 2.1)]
    [InlineData(2.04, -1, 2.0)]
    [InlineData(12.354, -2, 12.35)]
    [InlineData(12.0, -2, 12.0)]
    public void 修约照现役SimpleRoundTo(double value, int digit, double expected)
    {
        Assert.Equal(expected, MarshallAnalyzer.SimpleRoundTo(value, digit), 9);
    }
}
