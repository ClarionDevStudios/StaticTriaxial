namespace STS.Core.Tests;

using STS.Core;

/// <summary>
/// 显示抽稀：点数封顶，但**峰值/谷值/量程/首末点必须留住**（用户 2026-10-13 明确要求"不能丢峰值点"）。
/// 用户口径：抽稀只影响画面，落库曲线与判定输入仍是全量点。
/// </summary>
public sealed class CurveDownsamplerTests
{
    /// <summary>造一条"上升 → 峰值 → 下降"的典型"上升→峰值→下降"曲线，并在指定位置插一个尖峰。</summary>
    private static List<CurvePoint> BuildCurve(int count, int peakIndex, double peakLoad = 12.0)
    {
        var points = new List<CurvePoint>(count);
        for (var i = 0; i < count; i++)
        {
            var x = i * 0.01;                                   // 跨中挠度：每拍 0.01mm
            var y = i <= peakIndex
                ? 12.0 * i / Math.Max(1, peakIndex)             // 上升段
                : 12.0 * (count - i) / Math.Max(1, count - peakIndex); // 下降段
            points.Add(new CurvePoint(x, y));
        }

        points[peakIndex] = new CurvePoint(points[peakIndex].X, peakLoad);
        return points;
    }

    [Fact]
    public void 点数不超过上限()
    {
        var points = BuildCurve(5000, 2000);

        foreach (var max in new[] { 16, 100, 999, 2000 })
        {
            var result = CurveDownsampler.Downsample(points, max);
            Assert.True(
                result.Count <= max,
                $"上限 {max} 被突破：实际 {result.Count} 点"
            );
        }
    }

    [Fact]
    public void 点数本来就不超上限时原样返回()
    {
        var points = BuildCurve(300, 100);

        var result = CurveDownsampler.Downsample(points, 2000);

        Assert.Same(points, result);
    }

    [Theory]
    [InlineData(0)]        // 峰值就在第一个点
    [InlineData(1)]
    [InlineData(2500)]     // 正中间
    [InlineData(4999)]     // 最后一个点
    [InlineData(3777)]     // 随意位置
    public void 峰值点一定被保留(int peakIndex)
    {
        var points = BuildCurve(5000, peakIndex, peakLoad: 47.5);

        var result = CurveDownsampler.Downsample(points, 200);

        // 峰值点（Y 最大）必须原样出现在结果里
        Assert.Contains(result, p => p.X.Equals(points[peakIndex].X) && p.Y.Equals(47.5));
        Assert.Equal(47.5, result.Max(p => p.Y));
    }

    [Fact]
    public void 谷值与XY极值同样保留()
    {
        var points = BuildCurve(4096, 1500);
        // 造一个更低的谷底（破坏后的回落）与一个 X 更大的尾点
        points[3800] = new CurvePoint(points[3800].X, -0.35);

        var result = CurveDownsampler.Downsample(points, 128);

        Assert.Equal(points.Min(p => p.Y), result.Min(p => p.Y));   // 谷值不丢
        Assert.Equal(points.Max(p => p.Y), result.Max(p => p.Y));   // 峰值不丢
        Assert.Equal(points.Min(p => p.X), result.Min(p => p.X));   // 量程下限不缩
        Assert.Equal(points.Max(p => p.X), result.Max(p => p.X));   // 量程上限不缩
    }

    [Fact]
    public void 曲线起止点保留()
    {
        var points = BuildCurve(3000, 900);

        var result = CurveDownsampler.Downsample(points, 64);

        Assert.Equal(points[0], result[0]);
        Assert.Equal(points[^1], result[^1]);
    }

    [Fact]
    public void 多个并列峰值时保留最先出现的那个()
    {
        var points = BuildCurve(1000, 500, peakLoad: 30.0);
        points[500] = new CurvePoint(points[500].X, 30.0);
        points[520] = new CurvePoint(points[520].X, 30.0); // 并列峰值（靠后）

        var result = CurveDownsampler.Downsample(points, 32);

        Assert.Contains(result, p => p.X.Equals(points[500].X) && p.Y.Equals(30.0));
    }

    [Fact]
    public void 全平曲线也能抽稀且不丢端点()
    {
        var points = Enumerable
            .Range(0, 2000)
            .Select(i => new CurvePoint(i * 0.01, 5.0))
            .ToList();

        var result = CurveDownsampler.Downsample(points, 100);

        Assert.True(result.Count <= 100);
        Assert.Equal(5.0, result.Max(p => p.Y));
        Assert.Equal(points[0], result[0]);
        Assert.Equal(points[^1], result[^1]);
    }

    [Fact]
    public void 上限过小应报参数错误()
    {
        var points = BuildCurve(100, 50);

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => CurveDownsampler.Downsample(points, CurveDownsampler.MinimumPoints - 1)
        );
    }
}
