namespace ANX.Ui.Views.Pages;

using ANX.Core;
using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>试验执行页：把实时曲线接到 ViewModel 的采样点上。</summary>
public partial class TestExecutionPage : INavigableView<TestExecutionViewModel>
{
    private readonly List<CurvePoint> _points = [];

    public TestExecutionPage(TestExecutionViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        // 先把 ViewModel 拿到手（不要依赖 DataContextChanged），再订阅曲线事件
        viewModel.CurvePointAdded += OnCurvePointAdded;
        viewModel.CurveCleared += OnCurveCleared;

        RealtimePlot.Plot.XLabel("跨中挠度 (mm)");
        RealtimePlot.Plot.YLabel("荷载 (kN)");
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

    private void Redraw()
    {
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
