namespace ANX.Ui.Views.Pages;

using ANX.Core;
using ANX.Ui.Charts;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
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

        ApplyChartStyle(_theme.CurrentTheme);
    }

    public TestExecutionViewModel ViewModel { get; }

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
