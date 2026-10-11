namespace ANX.Ui.Views.Pages;

using ANX.Core;
using ANX.Core.Control;
using ANX.Ui.Charts;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Wpf.Ui.Abstractions.Controls;

/// <summary>
/// 试验执行页：把实时曲线接到 ViewModel 的采样点上。
/// 图的"行头"（轴名随语言、配色随主题）也在这里收拾 —— 这是视图特有的活，不适合放进 ViewModel。
/// </summary>
public partial class TestExecutionPage : INavigableView<TestExecutionViewModel>
{
    private readonly List<CurvePoint> _points = [];
    private readonly IThemeService _theme;

    /// <summary>上一次对齐用到的可见区高度（避免 LayoutUpdated 里反复设值）。</summary>
    private double _alignedToViewport = -1;

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

        // 先把 ViewModel 拿到手（不要依赖 DataContextChanged），再订阅曲线事件
        viewModel.CurvePointAdded += OnCurvePointAdded;
        viewModel.CurveCleared += OnCurveCleared;

        // 换主题 → 配色变；换语言 → 轴名变。两者都要重画一次。
        themeService.ThemeChanged += (_, themeName) => ApplyChartStyle(themeName);
        localizationService.LanguageChanged += (_, _) => ApplyChartStyle(_theme.CurrentTheme);

        // 本页不在前台时切了语言/主题，控件不会重绘（WpfPlot 留着旧位图）——
        // 每次重新显示都重刷一次，免得回来看到的是旧字。
        Loaded += (_, _) =>
        {
            ApplyChartStyle(_theme.CurrentTheme);
            ViewModel.BeginLiveReadout();

            // 盯住**可见区**的尺寸变化。注意不能盯页面的直接父元素（ContentPresenter）：
            // 它的高度跟着内容走，会形成环路。外层 ScrollViewer 的视口才是被约束住的真值。
            if (FindScrollHost() is { } scrollHost)
            {
                scrollHost.SizeChanged -= OnViewportChanged;
                scrollHost.SizeChanged += OnViewportChanged;
                scrollHost.LayoutUpdated -= OnViewportLayoutUpdated;
                scrollHost.LayoutUpdated += OnViewportLayoutUpdated;
            }

            UpdatePageHeight();
        };

        // 离开页面就退订实时刷新（换页后别再空转刷卡片）
        Unloaded += (_, _) => ViewModel.EndLiveReadout();

        // 窗口大小一变，重新把页面高度对到可见区
        SizeChanged += (_, _) => UpdatePageHeight();

        ApplyChartStyle(_theme.CurrentTheme);
    }

    public TestExecutionViewModel ViewModel { get; }

    private void OnViewportChanged(object sender, SizeChangedEventArgs e) => UpdatePageHeight();

    /// <summary>可见区每次布局完都对一次高（尺寸没变就跳过）。</summary>
    private void OnViewportLayoutUpdated(object? sender, EventArgs e)
    {
        var viewport = ViewportHeight();
        if (viewport > 0 && Math.Abs(viewport - _alignedToViewport) > 0.5)
        {
            ApplyPageHeight();
        }
    }

    /// <summary>
    /// 把本页高度限制在**内容宿主给的可见区**里（减掉页面自身的外边距）。
    ///
    /// 为什么必须限制：本页外面还有一层滚动容器，它按**无限高度**量内容，于是页内 Grid 的 `*`
    /// 行退化成"按内容取高"——操控信息一多，列表就无限长高、把整页撑高（实测：空列表 739，
    /// 追加 300 条后 12380）。给页面加上限之后，`*` 行拿到真实的可见高度，
    /// 列表自然就铺满"就绪"与"清空信息"之间那块，并在其中自己滚动。
    ///
    /// 取父元素（内容宿主）的高度而不是窗口高度：宿主已经把标题栏、导航头都扣掉了，正是我们要的那块。
    /// （纯 XAML 的 `RelativeSource AncestorType=Window` 在这个宿主持下取不到窗口，绑定会静默失效。）
    /// </summary>
    private void UpdatePageHeight() =>
        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(ApplyPageHeight)
        );

    private void ApplyPageHeight()
    {
        var viewport = ViewportHeight();
        if (viewport <= 0)
        {
            return;
        }

        _alignedToViewport = viewport;

        var margin = Margin;
        MaxHeight = Math.Max(0, viewport - margin.Top - margin.Bottom);
    }

    /// <summary>本页所在的外层滚动容器（WPF-UI 的 DynamicScrollViewer）。</summary>
    private ScrollViewer? FindScrollHost()
    {
        for (
            var node = VisualTreeHelper.GetParent(this);
            node is not null;
            node = VisualTreeHelper.GetParent(node)
        )
        {
            if (node is ScrollViewer scroll)
            {
                return scroll;
            }
        }

        return null;
    }

    /// <summary>
    /// 可见区高度：优先取外层 ScrollViewer 的视口高度（被 NavigationView 约束、不随内容变）；
    /// 万一没有滚动容器，就退回窗口内容区。
    /// </summary>
    private double ViewportHeight()
    {
        if (FindScrollHost() is { ViewportHeight: > 0 } scroll)
        {
            return scroll.ViewportHeight;
        }

        return Window.GetWindow(this)?.Content is FrameworkElement content
            ? content.ActualHeight
            : 0;
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

    private void OnCurvePointAdded(object? sender, CurvePoint point)
    {
        _points.Add(point);
        Redraw();
    }

    private void OnCurveCleared(object? sender, EventArgs e)
    {
        _points.Clear();
        Redraw();
    }

    /// <summary>轴名取当前语言、配色取当前主题，然后重画。</summary>
    private void ApplyChartStyle(string themeName)
    {
        RealtimePlot.Plot.XLabel(ViewModel.CurveXAxisTitle);
        RealtimePlot.Plot.YLabel(ViewModel.CurveYAxisTitle);

        CurveChartTheme.Apply(RealtimePlot, themeName);
    }

    private void Redraw()
    {
        // Clear() 只清曲线，轴名留着（ScottPlot 的 Clear 不动轴标签）
        RealtimePlot.Plot.Clear();

        if (_points.Count > 0)
        {
            RealtimePlot.Plot.Add.ScatterLine(
                _points.Select(p => p.X).ToArray(),
                _points.Select(p => p.Y).ToArray()
            );
            RealtimePlot.Plot.Axes.AutoScale();
        }

        RealtimePlot.Refresh();
    }
}
