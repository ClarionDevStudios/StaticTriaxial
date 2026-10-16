namespace STS.Ui.Charts;

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

    private static string? _chineseFont;

    /// <summary>
    /// 用一段中文样本让 ScottPlot 自己挑一款系统字体 —— 比写死"微软雅黑"稳：
    /// 客户工控机上不一定装了指定那一款。
    /// 离屏报表渲染器（`STS.Reporting.CurveImageRenderer`）用的是同一手法；两边各持一份，
    /// 是为了不让 Ui 去依赖 Reporting。
    /// </summary>
    public static void EnsureChineseFont() => Fonts.Default = ChineseFontName();

    /// <summary>
    /// 曲线**标注**（峰值文字 / 修正直线 / 修正原点…）用的墨色：取主题主文字色。
    /// 不能图省事用 ScottPlot 的默认字色 —— 那是黑的，深色主题下整条标注看不见。
    /// 主题资源取不到时回落白色（深色主题下的兜底，与 <c>Apply</c> 的兜底口径一致）。
    /// </summary>
    public static Color AnnotationColor() =>
        TryResolve("TextFillColorPrimaryBrush")
        ?? TryResolve("TextFillColorSecondaryBrush")
        ?? Color.FromHex("#FFFFFF");

    /// <summary>把当前主题的配色应用到图上，并把中文字体准备好。</summary>
    public static void Apply(WpfPlot plot, string themeName)
    {
        ArgumentNullException.ThrowIfNull(plot);

        EnsureChineseFont();

        var dark = !string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase);
        var style = plot.Plot.GetStyle();

        // 图背景 / 数据区：只用**不透明**的主题底色。
        // 注意别用 WinUI 的"卡片填充色"（CardBackgroundFillColorDefault 之类）—— 那是半透明白，
        // 当成不透明色会让深色主题下出现一整块白底（TryResolve 现在会跳过半透明画刷）。
        var figure = Resolve(
            ["ApplicationBackgroundBrush", "SolidBackgroundFillColorBaseBrush"],
            dark ? DarkFigureFallback : LightFigureFallback
        );
        var data = Resolve(
            ["SolidBackgroundFillColorBaseBrush", "LayerFillColorDefaultBrush", "ApplicationBackgroundBrush"],
            dark ? DarkDataFallback : LightDataFallback
        );

        style.FigureBackgroundColor = figure;
        style.DataBackgroundColor = data;

        // 文字色用**不透明**的主题文字画刷：WPF-UI 的次级文字色是半透明白，直接用会按不透明处理成黑。
        var textColor =
            TryResolve("TextFillColorPrimaryBrush") ?? TryResolve("TextFillColorSecondaryBrush");
        if (textColor is not null)
        {
            style.AxisColor = textColor;
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

        // 字体与字色必须**在 SetStyle 之后**装：SetStyle 会按样式把轴标签的字色刷回默认值
        // （实测：先设红再 SetStyle，最终字色是黑）。
        if (textColor is not null)
        {
            plot.Plot.Axes.Color(textColor.Value);
        }

        ApplyAxisText(plot.Plot, ChineseFontName(), textColor);
        plot.Refresh();
    }

    /// <summary>按候选键顺序取第一个能解析出来的主题颜色；全都没有就用兜底色。</summary>
    private static string ChineseFontName() =>
        _chineseFont ??= Fonts.Detect("跨中挠度 荷载 试件 试验");

    /// <summary>
    /// 把字体与字色装到图上所有会显示文字的标签（四轴刻度 + 轴名 + 标题 + 图例）。
    /// 两件事必须显式做，否则中文变方框 / 字色不对：
    /// ① 光设 <c>Fonts.Default</c> 不够 —— WpfPlot 在 `InitializeComponent` 时就建好了这些标签对象，
    /// 之后改静态默认值不会回头生效；
    /// ② <c>Axes.Color(...)</c> 只管刻度文字，**不管轴名**，轴名得单独给字色。
    /// </summary>
    private static void ApplyAxisText(Plot plot, string fontName, Color? textColor)
    {
        ApplyAxisText(plot.Axes.Bottom, fontName, textColor);
        ApplyAxisText(plot.Axes.Left, fontName, textColor);
        ApplyAxisText(plot.Axes.Top, fontName, textColor);
        ApplyAxisText(plot.Axes.Right, fontName, textColor);

        ApplyLabelText(plot.Axes.Title.Label, fontName, textColor);
        plot.Legend.FontName = fontName;
    }

    private static void ApplyAxisText(IAxis axis, string fontName, Color? textColor)
    {
        ApplyLabelText(axis.TickLabelStyle, fontName, textColor);
        ApplyLabelText(axis.Label, fontName, textColor);
    }

    private static void ApplyLabelText(LabelStyle label, string fontName, Color? textColor)
    {
        label.FontName = fontName;

        if (textColor is not null)
        {
            label.ForeColor = textColor.Value;
        }
    }

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
        // 半透明画刷直接当"取不到"：曲线图的底色不能用半透明色
        if (
            System.Windows.Application.Current?.TryFindResource(brushKey) is WpfSolidColorBrush brush
            && brush.Color.A == 0xFF
        )
        {
            return Color.FromHex($"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}");
        }

        return null;
    }
}
