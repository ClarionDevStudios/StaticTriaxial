namespace ANX.Ui.Views.Pages;

using ANX.Core;
using ANX.Ui.Charts;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>历史数据页：选中试件后把曲线画出来；轴名随语言、配色随主题。</summary>
public partial class HistoryPage : INavigableView<HistoryViewModel>
{
    private readonly IThemeService _theme;

    public HistoryPage(
        HistoryViewModel viewModel,
        IThemeService themeService,
        ILocalizationService localizationService
    )
    {
        ViewModel = viewModel;
        DataContext = this;
        _theme = themeService;

        InitializeComponent();

        viewModel.CurveLoaded += OnCurveLoaded;

        themeService.ThemeChanged += (_, themeName) => ApplyChartStyle(themeName);
        localizationService.LanguageChanged += (_, _) => ApplyChartStyle(_theme.CurrentTheme);

        // 同上（原因见 TestExecutionPage）：重新显示时重刷一次
        Loaded += (_, _) => ApplyChartStyle(_theme.CurrentTheme);

        ApplyChartStyle(_theme.CurrentTheme);
    }

    public HistoryViewModel ViewModel { get; }

    /// <summary>轴名取当前语言、配色取当前主题。</summary>
    private void ApplyChartStyle(string themeName)
    {
        CurvePlot.Plot.XLabel(ViewModel.CurveXAxisTitle);
        CurvePlot.Plot.YLabel(ViewModel.CurveYAxisTitle);

        CurveChartTheme.Apply(CurvePlot, themeName);
    }

    private void OnCurveLoaded(object? sender, IReadOnlyList<CurvePoint> curve)
    {
        CurvePlot.Plot.Clear();

        if (curve.Count > 0)
        {
            CurvePlot.Plot.Add.ScatterLine(
                curve.Select(p => p.X).ToArray(),
                curve.Select(p => p.Y).ToArray()
            );
            CurvePlot.Plot.Axes.AutoScale();
        }

        CurvePlot.Refresh();
    }
}
