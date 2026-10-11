namespace ANX.Ui.Views.Pages;

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
    }

    public SheetSelectionViewModel ViewModel { get; }
}
