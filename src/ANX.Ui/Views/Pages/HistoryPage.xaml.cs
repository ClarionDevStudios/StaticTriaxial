namespace ANX.Ui.Views.Pages;

using ANX.Control;
using ANX.Core;
using ANX.Ui.Charts;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>历史数据页：选中试件后把曲线画出来；轴名随语言、配色随主题。</summary>
public partial class HistoryPage : INavigableView<HistoryViewModel>
{
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;

    /// <summary>当前画在图上的一条曲线（换主题 / 换语言时按它重画，含标注）。</summary>
    private IReadOnlyList<CurvePoint> _curve = [];

    public HistoryPage(
        HistoryViewModel viewModel,
        IThemeService themeService,
        ILocalizationService localizationService
    )
    {
        ViewModel = viewModel;
        DataContext = this;
        _theme = themeService;
        _localization = localizationService;

        InitializeComponent();

        // 试验单列表的四列：列头按当前语言现建（DataGridColumn 不在可视化树里，
        // XAML 上的 DynamicResource 换语言后不会刷新）
        SheetListColumns.Build(SheetGrid, localizationService.GetString);

        viewModel.CurveLoaded += OnCurveLoaded;

        themeService.ThemeChanged += (_, themeName) => ApplyChartStyle(themeName);
        localizationService.LanguageChanged += (_, _) =>
        {
            SheetListColumns.Build(SheetGrid, localizationService.GetString);
            ApplyChartStyle(_theme.CurrentTheme);
        };

        // 同上（原因见 TestExecutionPage）：重新显示时重刷一次
        Loaded += (_, _) => ApplyChartStyle(_theme.CurrentTheme);

        // 主从表两个列表同样只让列表自己滚动，不撑高整页（与试验单页同源问题，用户 2026-10-12）
        PageViewportFitter.Attach(this);

        ApplyChartStyle(_theme.CurrentTheme);
    }

    public HistoryViewModel ViewModel { get; }

    /// <summary>轴名取当前语言、配色取当前主题。</summary>
    private void ApplyChartStyle(string themeName)
    {
        CurvePlot.Plot.XLabel(ViewModel.CurveXAxisTitle);
        CurvePlot.Plot.YLabel(ViewModel.CurveYAxisTitle);

        CurveChartTheme.Apply(CurvePlot, themeName);

        // 标注的文字/标记色取自主题，换主题后必须**整幅重画**一次，否则留着旧墨色
        // （深色主题下可能整条看不见）。曲线本身也跟着重画一次，代价可忽略。
        Redraw();
    }

    private void OnCurveLoaded(object? sender, IReadOnlyList<CurvePoint> curve)
    {
        _curve = curve;
        Redraw();
    }

    /// <summary>把当前曲线与标注画出来（加载新曲线 / 换主题 / 换语言都走这里）。</summary>
    private void Redraw()
    {
        CurvePlot.Plot.Clear();

        if (_curve.Count > 0)
        {
            // 与试验执行页同一套显示口径：先抽稀（保住峰值/谷值/量程），再 AutoScale，
            // 最后画标注（标注不参与量程，见 CurveAnnotator）。
            var display = CurveDownsampler.Downsample(_curve, MaxDisplayPoints());

            CurvePlot.Plot.Add.ScatterLine(
                display.Select(p => p.X).ToArray(),
                display.Select(p => p.Y).ToArray()
            );
            CurvePlot.Plot.Axes.AutoScale();
        }

        // 峰值点 + 修正直线 / 修正原点 / 拟合区间两端（与执行页共用同一个标注器，两处必然一致）
        CurveAnnotator.Draw(
            CurvePlot.Plot,
            _curve,
            ViewModel.SelectedAnnotation,
            CurveMarkTexts.From(_localization.GetString)
        );

        CurvePlot.Refresh();
    }

    /// <summary>
    /// 显示点数上限 = 绘图区像素宽 × 2（"每像素 1~2 点"），与执行页同一口径；
    /// 宽度还没量出来时用兜底值。只影响画面，库里与报表都是全量点。
    /// </summary>
    private int MaxDisplayPoints()
    {
        var width = CurvePlot.ActualWidth;
        var fromWidth = width > 0 ? (int)width * 2 : 0;
        return Math.Max(
            CurveDownsampler.MinimumPoints,
            fromWidth > 0 ? fromWidth : FallbackMaxDisplayPoints
        );
    }

    /// <summary>绘图区宽度量不出来时的兜底显示点数（与执行页同值）。</summary>
    private const int FallbackMaxDisplayPoints = 2000;
}
