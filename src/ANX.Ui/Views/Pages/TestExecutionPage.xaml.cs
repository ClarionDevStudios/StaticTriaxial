namespace ANX.Ui.Views.Pages;

using ANX.Control;
using ANX.Core;
using ANX.Core.Control;
using ANX.Ui.Charts;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

/// <summary>
/// 试验执行页：把实时曲线接到 ViewModel 的采样点上。
/// 图的"行头"（轴名随语言、配色随主题）也在这里收拾 —— 这是视图特有的活，不适合放进 ViewModel。
/// </summary>
public partial class TestExecutionPage : INavigableView<TestExecutionViewModel>
{
    private readonly List<CurvePoint> _points = [];
    private readonly IThemeService _theme;
    private readonly DispatcherTimer _redrawTimer;

    /// <summary>有没有攒着还没画的新点（节流用：收点只置脏，到点才真画）。</summary>
    private bool _redrawPending;

    /// <summary>试样完成后的标注内容（峰值点/修正直线/修正原点/拟合区间两端）；新一轮开始时清掉。</summary>
    private CurveAnnotation? _annotation;

    /// <summary>
    /// 显示重画的最小间隔。**收点与画图解耦**：控制循环 50Hz 产点，界面每 100ms 至多画一次
    /// （用户 2026-10-13 采用方案 1）—— 每秒少画 4/5 帧；曲线形态不受影响，因为攒下的点下一帧会补上。
    /// </summary>
    private static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>绘图区宽度量不出来时的兜底显示点数（"每像素 1~2 点"的口径）。</summary>
    private const int FallbackMaxDisplayPoints = 2000;

    public TestExecutionPage(
        TestExecutionViewModel viewModel,
        IThemeService themeService,
        ILocalizationService localizationService
    )
    {
        ViewModel = viewModel;
        DataContext = this;
        _theme = themeService;

        InitializeComponent();

        // 节流重画：收点只入列 + 置脏，由这个定时器按固定节奏真正重画一次。
        // 用 Render 优先级：跟渲染同批处理，不会插在输入/布局中间（与 IAP 的曲线演示页同一手法）。
        _redrawTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = RedrawInterval,
        };
        _redrawTimer.Tick += OnRedrawTick;

        // 先把 ViewModel 拿到手（不要依赖 DataContextChanged），再订阅曲线事件
        viewModel.CurvePointAdded += OnCurvePointAdded;
        viewModel.CurveCleared += OnCurveCleared;
        viewModel.CurveAnnotated += OnCurveAnnotated;

        // 换主题 → 配色变；换语言 → 轴名变。两者都要重画一次。
        themeService.ThemeChanged += (_, themeName) => ApplyChartStyle(themeName);
        localizationService.LanguageChanged += (_, _) => ApplyChartStyle(_theme.CurrentTheme);

        // 本页不在前台时切了语言/主题，控件不会重绘（WpfPlot 留着旧位图）——
        // 每次重新显示都重刷一次，免得回来看到的是旧字。
        Loaded += (_, _) =>
        {
            ApplyChartStyle(_theme.CurrentTheme);
            ViewModel.BeginLiveReadout();
            _redrawTimer.Start();
        };

        // 离开页面就退订实时刷新（换页后别再空转刷卡片）
        Unloaded += (_, _) =>
        {
            ViewModel.EndLiveReadout();
            _redrawTimer.Stop();      // 离开页面别让定时器继续跑
        };

        // 页面高度对到内容宿主的可见区：页内 `*` 行（操控信息列表等）才有真实高度、在页内自己滚动，
        // 而不是把整页撑高、让滚动落到外层宿主上（见 PageViewportFitter 的说明）。
        PageViewportFitter.Attach(this);

        ApplyChartStyle(_theme.CurrentTheme);
    }

    public TestExecutionViewModel ViewModel { get; }

    /// <summary>是否正在点动（避免"松开"与"失去鼠标捕获"各停一次、日志打两遍）。</summary>
    private bool _jogging;

    private void OnJogUpButtonPressed(object sender, MouseButtonEventArgs e) =>
        BeginJog(JogDirection.Up, sender);

    private void OnJogDownButtonPressed(object sender, MouseButtonEventArgs e) =>
        BeginJog(JogDirection.Down, sender);

    private void BeginJog(JogDirection direction, object sender)
    {
        if (_jogging || sender is not UIElement element)
        {
            return;
        }

        _jogging = true;

        // 自己抓鼠标捕获：这样即使松开时指针已移出按钮，也能收到 LostMouseCapture 把电停掉
        _ = element.CaptureMouse();
        _ = ViewModel.BeginJogAsync(direction);
    }

    private void OnJogButtonReleased(object sender, MouseEventArgs e) => EndJog();

    private void EndJog()
    {
        if (!_jogging)
        {
            return;
        }

        _jogging = false;
        _ = ViewModel.EndJogAsync();
    }

    /// <summary>收到一个要上屏的点：只入列 + 置脏，等定时器到点统一重画（节流）。</summary>
    private void OnCurvePointAdded(object? sender, CurvePoint point)
    {
        _points.Add(point);
        _redrawPending = true;
    }

    /// <summary>曲线被清空（新一轮试验 / 按参数重算）：这是低频事件，立刻画一次，让操作者马上看到空图。</summary>
    private void OnCurveCleared(object? sender, EventArgs e)
    {
        _points.Clear();
        _annotation = null; // 上一轮的峰值/修正直线跟着曲线一起清掉
        _redrawPending = false;
        Redraw();
    }

    /// <summary>试样完成：收下拟合信息并立刻重画一次（用户 2026-10-13：完成后才标注）。</summary>
    private void OnCurveAnnotated(object? sender, CurveAnnotation annotation)
    {
        _annotation = annotation;
        _redrawPending = false;
        Redraw();
    }

    private void OnRedrawTick(object? sender, EventArgs e)
    {
        if (!_redrawPending)
        {
            return;
        }

        _redrawPending = false;
        Redraw();
    }

    /// <summary>
    /// 显示点数上限 = 绘图区像素宽 × 2（"每像素 1~2 点"）；宽度还没量出来时用兜底值。
    /// 只影响画面：落库曲线与判定输入始终是全量点。
    /// </summary>
    private int MaxDisplayPoints()
    {
        var width = RealtimePlot.ActualWidth;
        var fromWidth = width > 0 ? (int)width * 2 : 0;
        return Math.Max(CurveDownsampler.MinimumPoints, fromWidth > 0 ? fromWidth : FallbackMaxDisplayPoints);
    }

    /// <summary>轴名取当前语言、配色取当前主题，然后重画。</summary>
    private void ApplyChartStyle(string themeName)
    {
        RealtimePlot.Plot.XLabel(ViewModel.CurveXAxisTitle);
        RealtimePlot.Plot.YLabel(ViewModel.CurveYAxisTitle);

        CurveChartTheme.Apply(RealtimePlot, themeName);

        // 标注的文字/标记色取自主题，换主题后必须整幅重画一次（否则留着旧墨色）
        Redraw();
    }

    private void Redraw()
    {
        // 抽稀后再画：点数封顶在"绘图区像素量级"，与试验时长脱钩；
        // 峰值/谷值/XY 极值由 CurveDownsampler 保证留住，所以 AutoScale 出来的量程与全量一致。
        var display = CurveDownsampler.Downsample(_points, MaxDisplayPoints());

        // Clear() 只清曲线，轴名留着（ScottPlot 的 Clear 不动轴标签）
        RealtimePlot.Plot.Clear();

        if (display.Count > 0)
        {
            RealtimePlot.Plot.Add.ScatterLine(
                display.Select(p => p.X).ToArray(),
                display.Select(p => p.Y).ToArray()
            );
            RealtimePlot.Plot.Axes.AutoScale();
        }

        // 标注用**全量**点找峰值（不是抽稀后的），并且放在 AutoScale 之后：
        // 修正直线在尾部的延长值通常远高于峰值，让它参与量程会把曲线压扁（见 CurveAnnotator 的说明）。
        CurveAnnotator.Draw(RealtimePlot.Plot, _points, _annotation);

        RealtimePlot.Refresh();
    }
}
