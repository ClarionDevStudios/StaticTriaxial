namespace ANX.Ui.Views.Pages;

using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>试样编辑页：编辑试件尺寸（跨径 / 宽度 / 高度）。</summary>
public partial class SampleEditorPage : INavigableView<SampleEditorViewModel>
{
    public SampleEditorPage(SampleEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();
    }

    public SampleEditorViewModel ViewModel { get; }
}
