namespace ANX.Ui.Views.Pages;

using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>系统设置页：语言、主题、日志目录、版本信息。</summary>
public partial class SettingsPage : INavigableView<SettingsViewModel>
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        viewModel.Initialize();
    }

    public SettingsViewModel ViewModel { get; }
}
