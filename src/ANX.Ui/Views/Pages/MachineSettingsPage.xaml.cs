namespace ANX.Ui.Views.Pages;

using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>参数配置页：编辑机器与控制参数（加载速率 / 接触阈值 / 破坏判据 / 确认帧数）。</summary>
public partial class MachineSettingsPage : INavigableView<MachineSettingsViewModel>
{
    public MachineSettingsPage(MachineSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public MachineSettingsViewModel ViewModel { get; }
}
