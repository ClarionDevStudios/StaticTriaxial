namespace ANX.Ui.Views.Pages;

using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>
/// 打印预览页（用户 2026-10-15）：报表在**窗体打开时**生成（按当前界面语言、同名静默覆盖），
/// 渲染成图显示，上方三个按钮 —— 打印报表 / 定位文件 / 导出PDF（暂置灰）。
/// </summary>
public partial class PrintPreviewPage : INavigableView<PrintPreviewViewModel>, IModalPage
{
    public PrintPreviewPage(PrintPreviewViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public PrintPreviewViewModel ViewModel { get; }

    /// <inheritdoc />
    public event EventHandler<bool>? CloseRequested
    {
        add => ViewModel.CloseRequested += value;
        remove => ViewModel.CloseRequested -= value;
    }

    /// <inheritdoc />
    public void OnDialogOpened() => ViewModel.OnOpened();
}
