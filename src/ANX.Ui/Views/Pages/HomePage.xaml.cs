namespace ANX.Ui.Views.Pages;

using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>首页。M1 阶段只展示骨架状态。</summary>
public partial class HomePage : INavigableView<HomeViewModel>
{
    public HomePage(HomeViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }
}
