namespace STS.Ui.Views.Pages;

using STS.Ui.Services;
using STS.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>
/// 参数配置页：编辑机器与控制参数（通道设置 / 试验参数）。
/// 用户 2026-10-17 起从导航栏「试验参数」进入（原先在执行页按钮里、当弹窗打开）；
/// 弹出窗体那套接口（<see cref="IModalPage"/>）保留不动 —— 想再当弹窗用时不缺东西。
/// </summary>
public partial class MachineSettingsPage : INavigableView<MachineSettingsViewModel>, IModalPage
{
    public MachineSettingsPage(MachineSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public MachineSettingsViewModel ViewModel { get; }

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
