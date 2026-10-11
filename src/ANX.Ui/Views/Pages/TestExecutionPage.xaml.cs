namespace ANX.Ui.Views.Pages;

using ANX.Core;
using ANX.Core.Control;
using ANX.Ui.Charts;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
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
        Loaded += (_, _) => ApplyChartStyle(_theme.CurrentTheme);

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
