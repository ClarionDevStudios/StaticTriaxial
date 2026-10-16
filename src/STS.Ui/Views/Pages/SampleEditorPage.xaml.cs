namespace STS.Ui.Views.Pages;

using STS.Ui.Services;
using STS.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>
/// 试样信息填录页：编辑"这一件试样"的信息（跨径 / 宽度 / 高度…）。
/// 用户 2026-10-13 起以**弹出窗体**方式打开（见 <see cref="IModalPageService"/>）。
/// </summary>
public partial class SampleEditorPage : INavigableView<SampleInfoViewModel>, IModalPage
{
    public SampleEditorPage(SampleInfoViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public SampleInfoViewModel ViewModel { get; }

    /// <summary>
    /// 把 ViewModel 的关窗请求转发给弹窗宿主（页面 DataContext 是自己，宿主拿不到 ViewModel）。
    /// </summary>
    public event EventHandler<bool>? CloseRequested
    {
        add => ViewModel.CloseRequested += value;
        remove => ViewModel.CloseRequested -= value;
    }

    /// <inheritdoc />
    public void OnDialogOpened() => ViewModel.OnOpened();
}
