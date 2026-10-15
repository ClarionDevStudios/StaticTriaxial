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
    public void 上升段单调且前段基本是直线()
    {
        var previous = 0.0;
        for (var d = 0.0; d <= PeakDeflection; d += 0.01)
        {
            var load = SampleCurve.LoadAt(d, Peak, PeakDeflection);
            Assert.True(load >= previous, $"挠度 {d:0.00} 处回落了");
            previous = load;
        }

        // 前 3/4 挠度基本线性（10%~80% 荷载区间都在这段上）——判定的"修正直线"拟合要靠它
        var quarter = SampleCurve.LoadAt(PeakDeflection / 4, Peak, PeakDeflection);
        var half = SampleCurve.LoadAt(PeakDeflection / 2, Peak, PeakDeflection);
        var threeQuarters = SampleCurve.LoadAt(PeakDeflection * 0.75, Peak, PeakDeflection);
        Assert.Equal(quarter * 2, half, 1e-9);
        Assert.Equal(quarter * 3, threeQuarters, 1e-9);

        // 只在最后一段圆下来（斜率变小）
        var slopeBefore = (threeQuarters - half) / (PeakDeflection * 0.25);
        var slopeAfter = (Peak - threeQuarters) / (PeakDeflection * 0.25);
        Assert.True(slopeAfter < slopeBefore, "接近峰值应当圆下来");
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
