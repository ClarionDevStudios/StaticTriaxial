namespace ANX.Communication.Tests.Channels;

using ANX.Communication.Channels;

/// <summary>
/// 模拟器的试件响应：用户 2026-10-15 要求"生成的数据更贴近实际"，
/// 于是从"时间线性折线"改成"**挠度的函数**"（上升段先快后圆 → 圆峰 → 陡降 → 低位尾巴）。
/// 这里钉住形状特征，免得以后又退化成一条直线。
/// </summary>
public sealed class SampleCurveTests
{
    private const double Peak = 1.5;
    private const double PeakDeflection = 0.7;

    [Fact]
    public void 接触前荷载为零()
    {
        Assert.Equal(0, SampleCurve.LoadAt(0, Peak, PeakDeflection));
        Assert.Equal(0, SampleCurve.LoadAt(-0.5, Peak, PeakDeflection));
    }

    [Fact]
    public void 峰值处正好是破坏荷载()
    {
        Assert.Equal(Peak, SampleCurve.LoadAt(PeakDeflection, Peak, PeakDeflection), 1e-9);
    }

    [Fact]
    public void 上升段单调_接触后先有坐实段再直线()
    {
        var previous = 0.0;
        for (var d = 0.0; d <= PeakDeflection; d += 0.01)
        {
            var load = SampleCurve.LoadAt(d, Peak, PeakDeflection);
            Assert.True(load >= previous, $"挠度 {d:0.00} 处回落了");
            previous = load;
        }

        // ① 接触后先有"坐实段"：挠度在走、荷载涨得慢 —— 用户 2026-10-15 要求这一段"适当延长"
        var seated = SampleCurve.LoadAt(PeakDeflection * 0.30, Peak, PeakDeflection);
        Assert.InRange(seated / Peak, 0.06, 0.10);

        // ② 之后是接近直线的上升段：等距挠度 → 等距荷载（10%~80% 荷载区间的拟合要靠它）
        var low = SampleCurve.LoadAt(PeakDeflection * 0.40, Peak, PeakDeflection);
        var middle = SampleCurve.LoadAt(PeakDeflection * 0.55, Peak, PeakDeflection);
        var high = SampleCurve.LoadAt(PeakDeflection * 0.70, Peak, PeakDeflection);
        Assert.Equal(middle - low, high - middle, 1e-9);

        // ③ 坐实段的斜率要明显低于上升段（否则不算"坐实"）
        var seatSlope = seated / (PeakDeflection * 0.30);
        var riseSlope = (high - low) / (PeakDeflection * 0.30);
        Assert.True(seatSlope < riseSlope / 3, $"坐实段该明显更平：{seatSlope:0.###} vs {riseSlope:0.###}");

        // 只在最后一段圆下来（斜率变小）
        var threeQuarters = SampleCurve.LoadAt(PeakDeflection * 0.75, Peak, PeakDeflection);
        var slopeBefore = (threeQuarters - high) / (PeakDeflection * 0.05);
        var slopeAfter = (Peak - threeQuarters) / (PeakDeflection * 0.25);
        Assert.True(slopeAfter < slopeBefore, "接近峰值应当圆下来");
    }

    /// <summary>
    /// 判定的"修正直线"拟合（参数表默认区间：峰值荷载的 10% ~ 80%）**整段落在直线段上** ——
    /// 坐实段一旦伸进这个区间，拟合的 R² 就会掉下来，判定报"无法拟合修正直线"。
    /// </summary>
    [Fact]
    public void 修正直线拟合区间整段落在直线段上()
    {
        var start = DeflectionAtLoad(Peak * 0.10);
        var end = DeflectionAtLoad(Peak * 0.80);

        // 区间两端都在坐实段之后、圆膝之前
        Assert.True(start > PeakDeflection * 0.30, $"10% 荷载点还在坐实段里：{start:0.####}mm");
        Assert.True(end < PeakDeflection * 0.75, $"80% 荷载点已经进了圆膝：{end:0.####}mm");

        // 区间内等距三点共线（R² = 1）
        var first = SampleCurve.LoadAt(start, Peak, PeakDeflection);
        var middle = SampleCurve.LoadAt((start + end) / 2, Peak, PeakDeflection);
        var last = SampleCurve.LoadAt(end, Peak, PeakDeflection);
        Assert.Equal((middle - first) * 2, last - first, 1e-9);
    }

    /// <summary>反查"荷载等于某个值"的挠度（只扫上升段，够用）。</summary>
    private static double DeflectionAtLoad(double loadKN)
    {
        var previous = 0.0;
        for (var d = 0.0; d <= PeakDeflection; d += 1e-5)
        {
            var load = SampleCurve.LoadAt(d, Peak, PeakDeflection);
            if (load >= loadKN)
            {
                return previous;
            }

            previous = d;
        }

        return PeakDeflection;
    }

    [Fact]
    public void 过峰后陡降然后低位尾巴()
    {
        // 过峰 9% 挠度：掉到四成上下（真机破坏后就是这个陡劲）
        var justAfter = SampleCurve.LoadAt(PeakDeflection * 1.09, Peak, PeakDeflection) / Peak;
        Assert.InRange(justAfter, 0.30, 0.50);

        // 过峰 50% 挠度：只剩百分之几的尾巴
        var tail = SampleCurve.LoadAt(PeakDeflection * 1.5, Peak, PeakDeflection) / Peak;
        Assert.InRange(tail, 0.04, 0.08);
    }

    [Fact]
    public void 全程不会出现负荷载()
    {
        // 旧模型在末尾会把荷载拉到 −26kN（假的）；新模型不允许出现负值
        for (var d = 0.0; d <= 20.0; d += 0.05)
        {
            Assert.True(SampleCurve.LoadAt(d, Peak, PeakDeflection) >= 0, $"挠度 {d:0.00} 处出现负荷载");
        }
    }

    [Fact]
    public void 平台行程按速率走_接触后再产生挠度()
    {
        // 50mm/min 走 1 秒 = 0.833mm 行程；接触空行程 0.8mm → 挠度约 0.033mm（还没什么荷载）
        var early = SampleCurve.At(seconds: 1.0, peakLoadKN: Peak, deflectionAtPeakMM: PeakDeflection);
        Assert.Equal(0.833, early.Displacement, 0.001);
        Assert.True(early.Load < 0.2, $"接触刚过不该有明显荷载，实际 {early.Load:0.###}");

        // 走到峰值（0.8 + 0.7 = 1.5mm 行程）用 1.8 秒
        var atPeak = SampleCurve.At(seconds: 1.8, peakLoadKN: Peak, deflectionAtPeakMM: PeakDeflection);
        Assert.Equal(Peak, atPeak.Load, 1e-6);
    }
}
