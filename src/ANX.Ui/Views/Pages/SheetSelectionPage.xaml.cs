namespace ANX.Ui.Views.Pages;

using ANX.Ui.Views;
using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>试验单页：新建或选择已有试验单，然后进执行页。</summary>
public partial class SheetSelectionPage : INavigableView<SheetSelectionViewModel>
{
    public SheetSelectionPage(SheetSelectionViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        // 已有试验单列表条目多时，只让**列表自己滚动**，不把整页撑高（用户 2026-10-12）
        PageViewportFitter.Attach(this);
    }

    public SheetSelectionViewModel ViewModel { get; }
}
