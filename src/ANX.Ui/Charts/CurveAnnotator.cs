namespace ANX.Ui.Charts;

using ANX.Control;
using ANX.Core;
using ScottPlot;

/// <summary>
/// 把标注画到 ScottPlot 图上（界面用）。**试验执行页与历史数据页共用这一份**，
/// 两处画出来的标注因此必然一致（用户 2026-10-13：两处保持一致）。
///
/// 画什么由 <see cref="CurveAnnotations"/> 算（纯几何 + 文案），本类只管"用 ScottPlot 落笔"：
/// **存盘 PNG（`ANX.Reporting.CurveImageRenderer`）照同一份几何画**，所以实时曲线与出图不会分叉
/// （用户 2026-10-15 报的缺陷：实时曲线有修正直线 / 修正原点 / 拟合区间两端，存盘图里没有）。
///
/// 量程口径：调用方应当**先**按曲线 <c>AutoScale()</c>，**再**调本方法、最后 <c>Refresh()</c>。
/// 修正直线在曲线尾部的延长值往往远高于峰值（切线本来就是上升段的外推），若让它参与自动量程，
/// Y 轴会被拉长、曲线被压扁；所以标注**不参与量程**，超出可视区的部分自然被裁掉。
/// </summary>
public static class CurveAnnotator
{
    /// <summary>
    /// 峰值点的高亮色。<b>不跟主题走</b>：它是"读数重点"，两种主题下都要跳出来；
    /// 橙红在白底与深色底上都够显眼（深浅主题通用的做法）。
    /// </summary>
    private static readonly Color PeakColor = Colors.OrangeRed;

    /// <summary>其它标注（修正直线 / 修正原点 / 拟合区间两端）用主题文字色，深浅主题都看得见。</summary>
    private static Color InkColor => CurveChartTheme.AnnotationColor();

    /// <summary>标记大小（像素）—— 存盘 PNG 用同一个数，两张图看起来才是一张图。</summary>
    private const float MarkerSize = 10;

    /// <summary>
    /// 在图上画峰值点与修正相关标注。<paramref name="annotation"/> 为 null 时只画峰值点。
    /// <paramref name="texts"/> 不给就用中文兜底（见 <see cref="CurveMarkTexts"/>）。
    /// </summary>
    public static void Draw(
        Plot plot,
        IReadOnlyList<CurvePoint> points,
        CurveAnnotation? annotation,
        CurveMarkTexts? texts = null
    )
    {
        ArgumentNullException.ThrowIfNull(plot);

        foreach (var mark in CurveAnnotations.Build(points, annotation, texts))
        {
            Draw(plot, mark);
        }
    }

    /// <summary>按标注的种类落笔：峰值实心圆、修正直线虚线、其余（修正原点 / 区间两端）空心圆。</summary>
    private static void Draw(Plot plot, CurveMark mark)
    {
        switch (mark.Kind)
        {
            case CurveMarkKind.Peak:
                _ = plot.Add.Marker(mark.X1, mark.Y1, MarkerShape.FilledCircle, MarkerSize, PeakColor);
                AddLabel(plot, mark.Label, mark.X1, mark.Y1, PeakColor);
                break;

            case CurveMarkKind.TangentLine:
                // 虚线 + 主题文字色：一眼与实测曲线分开
                var tangent = plot.Add.Line(mark.X1, mark.Y1, mark.X2, mark.Y2);
                tangent.Color = InkColor;
                tangent.LinePattern = LinePattern.Dashed;
                tangent.LineWidth = 1;
                break;

            case CurveMarkKind.Origin:
            case CurveMarkKind.FitBound:
                _ = plot.Add.Marker(mark.X1, mark.Y1, MarkerShape.OpenCircle, MarkerSize, InkColor);
                AddLabel(plot, mark.Label, mark.X1, mark.Y1, InkColor);
                break;
        }
    }

    /// <summary>
    /// 加一条标注文字，并把它**推离标记点**几个像素（默认位置正好压在标记上，读不清）。
    /// </summary>
    private static void AddLabel(
        Plot plot,
        string text,
        double x,
        double y,
        Color color
    )
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var label = plot.Add.Text(text, x, y);
        label.Color = color;
        label.FontSize = 12;
        label.OffsetX = 8;   // 像素
        label.OffsetY = -14;
        label.Alignment = Alignment.LowerLeft;
    }
}
