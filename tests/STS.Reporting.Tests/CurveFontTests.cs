namespace STS.Reporting.Tests;

/// <summary>
/// 曲线图里的中文不能渲染成方框 —— ScottPlot 默认字体不含中文字形，
/// 必须先用中文样本让库自己挑一款系统字体（界面与离屏渲染器各自调用同一手法）。
/// </summary>
public sealed class CurveFontTests
{
    [Fact]
    public void 配置中文字体后默认字体应无缺字()
    {
        CurveImageRenderer.EnsureChineseFont();

        var missing = new List<int>();
        var missingCount = ScottPlot.Fonts.CountMissingGlyphs(
            "跨中挠度 荷载 试件 试验 (mm) (kN)",
            missing
        );

        Assert.Equal(0, missingCount);
    }
}
