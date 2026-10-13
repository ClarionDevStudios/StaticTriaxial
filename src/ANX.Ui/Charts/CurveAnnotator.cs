namespace ANX.Ui.Charts;

using ANX.Control;
using ANX.Core;
using ScottPlot;

/// <summary>
/// 把 <see cref="CurveAnnotation"/> 画到 ScottPlot 图上。**试验执行页与历史数据页共用这一份**，
/// 两处画出来的标注因此必然一致（用户 2026-10-13：两处保持一致）。
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

    /// <summary>在图上画峰值点与修正相关标注。<paramref name="annotation"/> 为 null 时只画峰值点。</summary>
    public static void Draw(
        Plot plot,
        IReadOnlyList<CurvePoint> points,
        CurveAnnotation? annotation
    )
    {
        ArgumentNullException.ThrowIfNull(plot);

        if (points.Count == 0)
        {
            return;
        }

        var ink = InkColor;

        // ① 峰值点：曲线里 Y（荷载）最大的那个点
        var peak = points.MaxBy(point => point.Y);
        _ = plot.Add.Marker(peak.X, peak.Y, MarkerShape.FilledCircle, 10, PeakColor);
        AddLabel(
            plot,
            $"峰值 {peak.Y:0.00} kN / {peak.X:0.00} mm",
            peak.X,
            peak.Y,
            PeakColor
        );

        // ④ 关掉原点修正 → 没有拟合，到此为止
        if (annotation is not { HasFit: true } fit)
        {
            return;
        }

        // ② 修正直线：横跨整条曲线（右端取曲线最大 X），下端与 X 轴相交于修正原点。
        // 虚线 + 主题文字色：一眼与实测曲线分开。
        var maxX = points.Max(point => point.X);
        var tangent = plot.Add.Line(
            fit.OriginCorrection,
            0,
            maxX,
            (fit.TangentK * maxX) + fit.TangentB
        );
        tangent.Color = ink;
        tangent.LinePattern = LinePattern.Dashed;
        tangent.LineWidth = 1;

        // ③ 修正原点（直线与 X 轴交点）坐标值
        _ = plot.Add.Marker(fit.OriginCorrection, 0, MarkerShape.OpenCircle, 10, ink);
        AddLabel(
            plot,
            $"修正原点 {fit.OriginCorrection:0.00} mm",
            fit.OriginCorrection,
            0,
            ink
        );

        // ③ 拟合区间两端：峰值荷载 × 起/止百分比 —— 标在**曲线上**（用户 2026-10-13 更正：
        // 这两点是拟合区间的上下沿，落在实测曲线上，不是落在修正直线上），标注该处荷载值。
        // 曲线上升段没经过该荷载（异常数据）才回落到修正直线上求交点，免得标记掉到图外。
        foreach (var percent in new[] { fit.StartPercent, fit.EndPercent })
        {
            var load = peak.Y * percent / 100.0;
            var point =
                CurveDisplay.PointAtLoad(points, load) ?? new CurvePoint(fit.XAt(load), load);
            _ = plot.Add.Marker(point.X, point.Y, MarkerShape.OpenCircle, 10, ink);
            AddLabel(plot, $"{percent:0.#}% {load:0.00} kN", point.X, point.Y, ink);
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
        var label = plot.Add.Text(text, x, y);
        label.Color = color;
        label.FontSize = 12;
        label.OffsetX = 8;   // 像素
        label.OffsetY = -14;
        label.Alignment = Alignment.LowerLeft;
    }
}
