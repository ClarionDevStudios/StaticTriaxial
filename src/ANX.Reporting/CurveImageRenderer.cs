namespace ANX.Reporting;

using ANX.Control;
using ANX.Core;
using ScottPlot;

/// <summary>
/// 曲线图渲染成 PNG（无界面）：试验完成后每个试件存一张，供报表嵌入与现场留档。
/// 用 ScottPlot 的离屏渲染（SkiaSharp），不需要 WPF。
///
/// **标注**（峰值点 / 修正直线 / 修正原点 / 拟合区间两端）与实时曲线**共用同一份几何**
/// （<see cref="CurveAnnotations"/>）—— 用户 2026-10-15 报的缺陷就是"实时曲线画了、存盘图里没有"。
/// 落笔这一段在界面侧（`ANX.Ui.Charts.CurveAnnotator`）另有一份：界面要跟随深浅主题取墨色，
/// 离屏出的是白底图、固定用黑，两边各写各的画法，但几何与文案只有 `ANX.Control` 那一份。
/// </summary>
public sealed class CurveImageRenderer
{
    private static bool _fontConfigured;

    /// <summary>标记大小（像素）—— 与界面同一个数，两张图看起来才是一张图。</summary>
    private const float MarkerSize = 10;

    /// <summary>标注字号 —— 同上，与界面 `<c>CurveAnnotator</c>` 一致。</summary>
    private const float MarkFontSize = 12;

    /// <summary>修正直线 / 修正原点 / 区间两端用的墨色：离屏图是白底，固定黑（不跟界面主题走）。</summary>
    private static readonly Color InkColor = Colors.Black;

    /// <summary>峰值点的高亮色：与界面同一支橙红，"读数重点"在白底上也要跳出来。</summary>
    private static readonly Color PeakColor = Colors.OrangeRed;

    public CurveImageRenderer(int width = 900, int height = 600)
    {
        Width = width;
        Height = height;
        EnsureChineseFont();
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// ScottPlot 默认字体不含中文字形，中文标签会渲染成方块 —— 用中文样本让库自己挑一款系统字体。
    /// 比写死"微软雅黑"稳：客户工控机上不一定装了指定那一款。
    /// </summary>
    public static void EnsureChineseFont()
    {
        if (_fontConfigured)
        {
            return;
        }

        Fonts.Default = Fonts.Detect("跨中挠度 荷载 试件 试验");
        _fontConfigured = true;
    }

    /// <summary>
    /// 把一条曲线存成 PNG。<paramref name="annotation"/> 给了就把修正相关标注一起画上
    /// （与实时曲线同口径）；<paramref name="markTexts"/> 控制标注文案的语言，不给用中文兜底。
    /// </summary>
    public void SavePng(
        IReadOnlyList<CurvePoint> curve,
        string outputPath,
        string title,
        string xLabel,
        string yLabel,
        CurveAnnotation? annotation = null,
        CurveMarkTexts? markTexts = null
    )
    {
        ArgumentNullException.ThrowIfNull(curve);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plot = new Plot();
        if (curve.Count > 0)
        {
            plot.Add.ScatterLine(curve.Select(p => p.X).ToArray(), curve.Select(p => p.Y).ToArray());

            // 先按曲线定量程，**再**画标注：修正直线在尾部的延长值远高于峰值，
            // 让它参与自动量程会把曲线压扁（口径与界面侧 CurveAnnotator 的说明一致）。
            plot.Axes.AutoScale();
        }

        plot.Title(title);
        plot.XLabel(xLabel);
        plot.YLabel(yLabel);

        DrawMarks(plot, CurveAnnotations.Build(curve, annotation, markTexts));

        plot.SavePng(outputPath, Width, Height);
    }

    /// <summary>按标注的种类落笔（与界面侧同一套画法：峰值实心圆、修正直线虚线、其余空心圆）。</summary>
    private static void DrawMarks(Plot plot, IReadOnlyList<CurveMark> marks)
    {
        foreach (var mark in marks)
        {
            switch (mark.Kind)
            {
                case CurveMarkKind.Peak:
                    _ = plot.Add.Marker(
                        mark.X1,
                        mark.Y1,
                        MarkerShape.FilledCircle,
                        MarkerSize,
                        PeakColor
                    );
                    AddLabel(plot, mark.Label, mark.X1, mark.Y1, PeakColor);
                    break;

                case CurveMarkKind.TangentLine:
                    var tangent = plot.Add.Line(mark.X1, mark.Y1, mark.X2, mark.Y2);
                    tangent.Color = InkColor;
                    tangent.LinePattern = LinePattern.Dashed;
                    tangent.LineWidth = 1;
                    break;

                case CurveMarkKind.Origin:
                case CurveMarkKind.FitBound:
                    _ = plot.Add.Marker(
                        mark.X1,
                        mark.Y1,
                        MarkerShape.OpenCircle,
                        MarkerSize,
                        InkColor
                    );
                    AddLabel(plot, mark.Label, mark.X1, mark.Y1, InkColor);
                    break;
            }
        }
    }

    /// <summary>加一条标注文字，并把它**推离标记点**几个像素 —— 与界面侧同一套偏移量。</summary>
    private static void AddLabel(Plot plot, string text, double x, double y, Color color)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var label = plot.Add.Text(text, x, y);
        label.Color = color;
        label.FontSize = MarkFontSize;
        label.OffsetX = 8;   // 像素
        label.OffsetY = -14;
        label.Alignment = Alignment.LowerLeft;
    }
}
