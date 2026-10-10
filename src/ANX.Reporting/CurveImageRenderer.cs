namespace ANX.Reporting;

using ANX.Core;
using ScottPlot;

/// <summary>
/// 曲线图渲染成 PNG（无界面）：试验完成后每个试件存一张，供报表嵌入与现场留档。
/// 用 ScottPlot 的离屏渲染（SkiaSharp），不需要 WPF。
/// </summary>
public sealed class CurveImageRenderer
{
    private static bool _fontConfigured;

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

    /// <summary>把一条曲线存成 PNG。</summary>
    public void SavePng(
        IReadOnlyList<CurvePoint> curve,
        string outputPath,
        string title,
        string xLabel,
        string yLabel
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
        }

        plot.Title(title);
        plot.XLabel(xLabel);
        plot.YLabel(yLabel);
        plot.SavePng(outputPath, Width, Height);
    }
}
