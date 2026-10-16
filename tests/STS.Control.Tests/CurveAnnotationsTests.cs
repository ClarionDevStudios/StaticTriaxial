namespace STS.Control.Tests;

using STS.Control;
using STS.Core;

/// <summary>
/// 曲线标注的几何与文案（<see cref="CurveAnnotations"/>）。**这一份是界面与存盘 PNG 共用的**
/// —— 用户 2026-10-15 报的缺陷就是两处各画各的、存盘图里漏了标注，所以这里把"画什么"钉住。
/// </summary>
public sealed class CurveAnnotationsTests
{
    /// <summary>一条上升—峰值—破坏的曲线（荷载 kN / 跨中挠度 mm）。峰值 = 2.0 kN @ 0.6 mm。</summary>
    private static List<CurvePoint> Curve() =>
        [new(0, 0), new(0.2, 0.6), new(0.4, 1.4), new(0.6, 2.0), new(0.8, 1.5), new(1.0, 0.9)];

    /// <summary>拟合结果：直线 y = 4x − 0.1 → 与 X 轴交于 x = 0.025（修正原点）。</summary>
    private static CurveAnnotation Fit() => new(4.0, -0.1, 0.025);

    [Fact]
    public void 峰值点总要画_关掉原点修正时就只有它()
    {
        // 判定器关掉原点修正时斜率记 0 且没有拟合（HasFit = false）
        var marks = CurveAnnotations.Build(Curve(), new CurveAnnotation(0, 0, 0));

        var peak = Assert.Single(marks);
        Assert.Equal(CurveMarkKind.Peak, peak.Kind);
        Assert.Equal(0.6, peak.X1, 6);
        Assert.Equal(2.0, peak.Y1, 6);
        Assert.Equal("峰值 2.00 kN / 0.60 mm", peak.Label);
    }

    [Fact]
    public void 没有标注信息时也画峰值点()
    {
        var marks = CurveAnnotations.Build(Curve(), annotation: null);

        Assert.Equal(CurveMarkKind.Peak, Assert.Single(marks).Kind);
    }

    [Fact]
    public void 空曲线没有标注()
    {
        Assert.Empty(CurveAnnotations.Build([], Fit()));
    }

    [Fact]
    public void 有拟合时是五条_峰值_直线_修正原点_区间两端()
    {
        var marks = CurveAnnotations.Build(Curve(), Fit());

        CurveMarkKind[] expected =
        [
            CurveMarkKind.Peak,
            CurveMarkKind.TangentLine,
            CurveMarkKind.Origin,
            CurveMarkKind.FitBound,
            CurveMarkKind.FitBound,
        ];

        Assert.Equal(expected, marks.Select(mark => mark.Kind));
    }

    [Fact]
    public void 修正直线从修正原点横跨到曲线最右()
    {
        var line = CurveAnnotations.Build(Curve(), Fit())[1];

        // 左端落在 X 轴上（即修正原点），右端取曲线最大 X 处的外推值
        Assert.Equal(0.025, line.X1, 6);
        Assert.Equal(0.0, line.Y1, 6);
        Assert.Equal(1.0, line.X2, 6);
        Assert.Equal(3.9, line.Y2, 6); // 4 × 1.0 − 0.1
        Assert.Equal(string.Empty, line.Label); // 直线只有几何、不写字
    }

    [Fact]
    public void 修正原点标在X轴上()
    {
        var origin = CurveAnnotations.Build(Curve(), Fit())[2];

        Assert.Equal(0.025, origin.X1, 6);
        Assert.Equal(0.0, origin.Y1, 6);
        Assert.Equal("修正原点 0.03 mm", origin.Label);
    }

    [Fact]
    public void 区间两端落在曲线上_标的是该处荷载()
    {
        var marks = CurveAnnotations.Build(Curve(), Fit());

        // 峰值 2.0 kN 的 10% = 0.2 kN：上升段首段中点 → x ≈ 0.0667
        var start = marks[3];
        Assert.Equal(0.2, start.Y1, 6);
        Assert.Equal(0.0667, start.X1, 3);
        Assert.Equal("10% 0.20 kN", start.Label);

        // 80% = 1.6 kN：落在 (0.4,1.4)~(0.6,2.0) 之间 → x ≈ 0.4667
        var end = marks[4];
        Assert.Equal(1.6, end.Y1, 6);
        Assert.Equal(0.4667, end.X1, 3);
        Assert.Equal("80% 1.60 kN", end.Label);
    }

    [Fact]
    public void 区间百分比按标注里的取值()
    {
        // 参数表把区间改成 20% / 90%（用户可调）—— 两端标记跟着走
        var marks = CurveAnnotations.Build(Curve(), Fit() with { StartPercent = 20, EndPercent = 90 });

        Assert.Equal("20% 0.40 kN", marks[3].Label);
        Assert.Equal("90% 1.80 kN", marks[4].Label);
    }

    [Fact]
    public void 文案可以换语言_数字不随区域设置变()
    {
        var texts = new CurveMarkTexts("Peak {0:0.00} / {1:0.00}", "Origin {0:0.00}", "{0:0.#}% {1:0.00}");

        var marks = CurveAnnotations.Build(Curve(), Fit(), texts);

        Assert.Equal("Peak 2.00 / 0.60", marks[0].Label);
        Assert.Equal("Origin 0.03", marks[2].Label);
        Assert.Equal("80% 1.60", marks[4].Label);
    }
}
