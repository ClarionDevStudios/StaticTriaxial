namespace ANX.Ui.Views.Pages;

using ANX.Core;
using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>历史数据页：选中试件后把曲线画出来。</summary>
public partial class HistoryPage : INavigableView<HistoryViewModel>
{
    public HistoryPage(HistoryViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        viewModel.CurveLoaded += OnCurveLoaded;

        CurvePlot.Plot.XLabel("跨中挠度 (mm)");
        CurvePlot.Plot.YLabel("荷载 (kN)");
    }

    public HistoryViewModel ViewModel { get; }

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
