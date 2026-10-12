namespace ANX.Ui.Views.Pages;

using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>试样信息填录页：编辑"这一件试样"的信息（跨径 / 宽度 / 高度…）。</summary>
public partial class SampleEditorPage : INavigableView<SampleInfoViewModel>
{
    public SampleEditorPage(SampleInfoViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public SampleInfoViewModel ViewModel { get; }
}
