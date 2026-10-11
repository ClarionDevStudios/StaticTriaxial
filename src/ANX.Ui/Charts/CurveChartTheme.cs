namespace ANX.Ui.Charts;

using ScottPlot;
using ScottPlot.WPF;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

/// <summary>
/// 曲线图（ScottPlot）的字体与配色适配器。有两件事 ScottPlot 自己不管，必须由界面接上：
/// <list type="number">
///   <item>默认字体没有中文字形 —— 中文轴标签会渲染成方框；</item>
///   <item>图背景 / 数据区 / 坐标轴与文字颜色不随 WPF 主题变化。</item>
/// </list>
/// 换主题或换语言后重新调用 <see cref="Apply"/> 即可（页面在事件里调）。
/// </summary>
public static class CurveChartTheme
{
    /// <summary>
    /// 主题资源里没有画刷时的兜底颜色。只在 WPF-UI 的主题字典取不到对应用画刷时用，
    /// 保证"深色主题下不会出现一整块白底图"。取到资源时一律用资源值。
    /// </summary>
    private const string DarkFigureFallback = "#1E1E1E";

    private const string DarkDataFallback = "#252525";
    private const string LightFigureFallback = "#FFFFFF";
    private const string LightDataFallback = "#FAFAFA";

    private static bool _fontConfigured;

    /// <summary>
    /// 用一段中文样本让 ScottPlot 自己挑一款系统字体 —— 比写死"微软雅黑"稳：
    /// 客户工控机上不一定装了指定那一款。
    /// 离屏报表渲染器（`ANX.Reporting.CurveImageRenderer`）用的是同一手法；两边各持一份，
    /// 是为了不让 Ui 去依赖 Reporting。
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

    /// <summary>把当前主题的配色应用到图上，并把中文字体准备好。</summary>
    public static void Apply(WpfPlot plot, string themeName)
    {
        ArgumentNullException.ThrowIfNull(plot);

        EnsureChineseFont();

        var dark = !string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase);
        var style = plot.Plot.GetStyle();

        // 图背景 / 数据区：优先用卡片背景，退到窗口背景，最后才是兜底色
        var figure = Resolve(
            ["ApplicationBackgroundBrush", "SolidBackgroundFillColorBaseBrush"],
            dark ? DarkFigureFallback : LightFigureFallback
        );
        var data = Resolve(
            ["CardBackgroundFillColorDefaultBrush", "ControlFillColorDefaultBrush", "ApplicationBackgroundBrush"],
            dark ? DarkDataFallback : LightDataFallback
        );

        style.FigureBackgroundColor = figure;
        style.DataBackgroundColor = data;

        // 坐标轴线与刻度文字、网格、图例：主题里没有就保持 ScottPlot 原值（不硬编码）
        var axis = TryResolve("TextFillColorSecondaryBrush");
        if (axis is not null)
        {
            style.AxisColor = axis;
            plot.Plot.Axes.Color(axis.Value);
        }

        var grid = TryResolve("ControlStrokeColorDefaultBrush");
        if (grid is not null)
        {
            style.GridMajorLineColor = grid;
        }

        var legendText = TryResolve("TextFillColorPrimaryBrush");
        if (legendText is not null)
        {
            style.LegendFontColor = legendText;
            style.LegendOutlineColor = legendText;
        }

        // data 一定有值（Resolve 带兜底色）
        style.LegendBackgroundColor = data;

        plot.Plot.SetStyle(style);
        plot.Refresh();
    }

    /// <summary>按候选键顺序取第一个能解析出来的主题颜色；全都没有就用兜底色。</summary>
    private static Color Resolve(string[] brushKeys, string fallbackHex)
    {
        foreach (var key in brushKeys)
        {
            var color = TryResolve(key);
            if (color is not null)
            {
                return color.Value;
            }
        }

        return Color.FromHex(fallbackHex);
    }

    /// <summary>
    /// 从应用资源里取一个画刷的颜色（主题切换后资源值会变，所以每次都要现取）。
    /// 画刷带透明度时按不透明处理 —— 曲线图不需要半透明底。
    /// </summary>
    private static Color? TryResolve(string brushKey)
    {
        if (System.Windows.Application.Current?.TryFindResource(brushKey) is WpfSolidColorBrush brush)
        {
            return Color.FromHex($"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}");
        }

        return null;
    }
}
