namespace ANX.Ui.Views.Pages;

using System.ComponentModel;
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
    private readonly ILocalizationService _localization;
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
        _localization = localizationService;

        InitializeComponent();

        BuildSampleColumns();

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

        // 运行指示动画跟着"跑没跑试验"开 / 停
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 换主题 → 配色变；换语言 → 轴名变。两者都要重画一次。
        themeService.ThemeChanged += (_, themeName) => ApplyChartStyle(themeName);
        localizationService.LanguageChanged += (_, _) =>
        {
            // 试样列表的表头也是文字（试件号 / 跨径 L …）：换语言要整列重建一遍
            BuildSampleColumns();
            ApplyChartStyle(_theme.CurrentTheme);
        };

        // 本页不在前台时切了语言/主题，控件不会重绘（WpfPlot 留着旧位图）——
        // 每次重新显示都重刷一次，免得回来看到的是旧字。
        Loaded += (_, _) =>
        {
            ApplyChartStyle(_theme.CurrentTheme);
            ViewModel.BeginLiveReadout();
            _redrawTimer.Start();
            UpdateRunningAnimation(); // 从别的页回来时按当前状态对齐（可能正在跑）
        };

        // 离开页面就退订实时刷新（换页后别再空转刷卡片）
        Unloaded += (_, _) =>
        {
            ViewModel.EndLiveReadout();
            _redrawTimer.Stop();      // 离开页面别让定时器继续跑
            StopRunningAnimation();   // 不在前台了就别转
        };

        // 页面高度对到内容宿主的可见区：页内 `*` 行（操控信息列表等）才有真实高度、在页内自己滚动，
        // 而不是把整页撑高、让滚动落到外层宿主上（见 PageViewportFitter 的说明）。
        PageViewportFitter.Attach(this);

        ApplyChartStyle(_theme.CurrentTheme);
    }

    public TestExecutionViewModel ViewModel { get; }

    /// <summary>
    /// 运行指示动画：匀速转一圈 1.2 秒、无限循环（用户 2026-10-13：
    /// 试验过程中开启动画，否则停止动画）。
    /// </summary>
    private static readonly System.Windows.Media.Animation.DoubleAnimation SpinnerAnimation = new(
        0,
        360,
        TimeSpan.FromSeconds(1.2)
    )
    {
        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
    };

    /// <summary>按运行状态开 / 停动画。<b>停</b>是把动画时钟摘掉（不是暂停），角度复位到 0。</summary>
    private void UpdateRunningAnimation()
    {
        if (ViewModel.IsRunning)
        {
            RunningSpinnerRotate.BeginAnimation(
                System.Windows.Media.RotateTransform.AngleProperty,
                SpinnerAnimation
            );
        }
        else
        {
            StopRunningAnimation();
        }
    }

    /// <summary>停动画：把角度上的动画时钟摘掉（真正停下，不是暂停），角度复位 0。</summary>
    private void StopRunningAnimation()
    {
        RunningSpinnerRotate.BeginAnimation(
            System.Windows.Media.RotateTransform.AngleProperty,
            null
        );
        RunningSpinnerRotate.Angle = 0;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 选中项变了（例如"新建试样信息"存完后由 ViewModel 选中刚建的那件）→ 把那一行滚进可视区
        if (
            string.Equals(e.PropertyName, nameof(TestExecutionViewModel.SelectedSample), StringComparison.Ordinal)
            && ViewModel.SelectedSample is { } selected
        )
        {
            SampleGrid.ScrollIntoView(selected);
        }

        if (string.Equals(e.PropertyName, nameof(TestExecutionViewModel.IsRunning), StringComparison.Ordinal))
        {
            UpdateRunningAnimation();
        }
    }

    /// <summary>
    /// 按**方法声明的试样信息字段**生成试样列表的列（用户 2026-10-13）。
    /// 列数随方法变（弯曲是 试件号 + 跨径 / 宽 / 高），所以只能在 code-behind 里建；
    /// 单元格绑定到行的下标（行就是 <c>string[]</c>，列序与 ViewModel 的表头一致）。
    /// </summary>
    private void BuildSampleColumns()
    {
        // 换语言时整列重建，所以先清空（表头文字按当前语言现取，见 SampleColumnHeaders）
        SampleGrid.Columns.Clear();

        var headers = ViewModel.SampleColumnHeaders;
        for (var i = 0; i < headers.Count; i++)
        {
            SampleGrid.Columns.Add(
                new DataGridTextColumn
                {
                    Header = headers[i],
                    Binding = new System.Windows.Data.Binding($"Cells[{i}]"),
                    Width = i == 0
                        ? new DataGridLength(1, DataGridLengthUnitType.Auto)
                        : new DataGridLength(1, DataGridLengthUnitType.Star),
                }
            );
        }
    }

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
        // 标注文字按**当前界面语言**取（同一条标注的画面与存盘图用同一组资源键）。
        CurveAnnotator.Draw(
            RealtimePlot.Plot,
            _points,
            _annotation,
            CurveMarkTexts.From(_localization.GetString)
        );

        RealtimePlot.Refresh();
    }
}
