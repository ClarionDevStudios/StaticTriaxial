namespace ANX.Method.Marshall.Tests;

/// <summary>
/// 试件高度修正系数（照现役 `Common\MarshallCorrectionFactor.pas` 搬）。钉四件事：
/// 标准高度 = 1.00、命中表格点用原值、两点之间线性插值、**两位小数**；以及越界三种策略。
/// </summary>
public sealed class MarshallCorrectionFactorTests
{
    [Theory]
    [InlineData(MarshallSpecimenSize.Small100mm, 63.5)]
    [InlineData(MarshallSpecimenSize.Large150mm, 95.2)]
    public void 标准高度的修正系数是1(MarshallSpecimenSize size, double heightMM)
    {
        Assert.True(MarshallCorrectionFactor.TryGet(heightMM, size, out var factor));
        Assert.Equal(1.00, factor, 6);
    }

    [Theory]
    [InlineData(25.4, 5.56)]
    [InlineData(28.6, 4.55)]
    [InlineData(49.2, 1.56)]
    [InlineData(61.9, 1.04)]
    [InlineData(76.2, 0.76)]
    public void 命中表格点用表格原值(double heightMM, double expected)
    {
        Assert.True(
            MarshallCorrectionFactor.TryGet(heightMM, MarshallSpecimenSize.Small100mm, out var factor)
        );
        Assert.Equal(expected, factor, 6);
    }

    [Fact]
    public void 两点之间线性插值并取两位小数()
    {
        // 26.2 正好在 25.4(5.56) 与 27.0(5.00) 之间 → 5.28
        Assert.True(MarshallCorrectionFactor.TryGet(26.2, MarshallSpecimenSize.Small100mm, out var mid));
        Assert.Equal(5.28, mid, 6);

        // 63.0 在 61.9(1.04) 与 63.5(1.00) 之间 → 1.04 + (1.1/1.6)×(−0.04) = 1.0125 → 1.01
        Assert.True(MarshallCorrectionFactor.TryGet(63.0, MarshallSpecimenSize.Small100mm, out var near));
        Assert.Equal(1.01, near, 6);
    }

    [Fact]
    public void 越大口径的表也照现役表值()
    {
        Assert.True(MarshallCorrectionFactor.TryGet(88.9, MarshallSpecimenSize.Large150mm, out var low));
        Assert.Equal(1.12, low, 6);

        // 92.9 在 92.1(1.06) 与 93.7(1.03) 之间 → 1.045 → 1.05（四舍五入）
        Assert.True(MarshallCorrectionFactor.TryGet(92.9, MarshallSpecimenSize.Large150mm, out var mid));
        Assert.Equal(1.05, mid, 6);
    }

    [Fact]
    public void 越界默认截断_拒绝与外推两种策略也照现役()
    {
        // 默认（Clamp）：低于最小值取端点系数、高于最大值取另一端
        Assert.True(MarshallCorrectionFactor.TryGet(20.0, MarshallSpecimenSize.Small100mm, out var below));
        Assert.Equal(5.56, below, 6);
        Assert.True(MarshallCorrectionFactor.TryGet(90.0, MarshallSpecimenSize.Small100mm, out var above));
        Assert.Equal(0.76, above, 6);

        // Reject：越界回 false
        Assert.False(
            MarshallCorrectionFactor.TryGet(
                20.0,
                MarshallSpecimenSize.Small100mm,
                out _,
                MarshallOutOfBoundsPolicy.Reject
            )
        );

        // Extrapolate：按边缘两点外推（25.4/5.56 与 27.0/5.00 的斜率 = −0.35/mm）
        Assert.True(
            MarshallCorrectionFactor.TryGet(
                25.0,
                MarshallSpecimenSize.Small100mm,
                out var extrapolated,
                MarshallOutOfBoundsPolicy.Extrapolate
            )
        );
        Assert.Equal(5.70, extrapolated, 6);
    }

    [Fact]
    public void 两张表的点数与现役一致_且系数随高度单调不增()
    {
        Assert.Equal(33, MarshallCorrectionFactor.Table100Size);
        Assert.Equal(9, MarshallCorrectionFactor.Table150Size);

        var previous = double.MaxValue;
        for (var height = 25.4; height <= 76.2 + 1e-9; height += 0.2)
        {
            Assert.True(MarshallCorrectionFactor.TryGet(height, MarshallSpecimenSize.Small100mm, out var factor));
            Assert.True(factor <= previous + 1e-9, $"{height:0.0} 处系数回升了：{previous} → {factor}");
            previous = factor;
        }
    }
}
