namespace ANX.Ui.Views.Pages;

using ANX.Ui.Services;
using ANX.Ui.Views;
using ANX.Ui.ViewModels;
using Wpf.Ui.Abstractions.Controls;

/// <summary>
/// 试验单页：新建或选择已有试验单（用户 2026-10-13 起以**弹出窗体**方式打开，
/// 见 <see cref="IModalPageService"/>）。
/// </summary>
public partial class SheetSelectionPage : INavigableView<SheetSelectionViewModel>, IModalPage
{
    public SheetSelectionPage(
        SheetSelectionViewModel viewModel,
        ILocalizationService localizationService
    )
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        // 试验单列表的四列：列头按当前语言现建（DataGridColumn 不在可视化树里，
        // XAML 上的 DynamicResource 换语言后不会刷新 —— 用户 2026-10-15 报的那条）
        SheetListColumns.Build(SheetGrid, localizationService.GetString);
        localizationService.LanguageChanged += (_, _) =>
            SheetListColumns.Build(SheetGrid, localizationService.GetString);

        // 已有试验单列表条目多时，只让**列表自己滚动**，不把整页撑高（用户 2026-10-12）
        PageViewportFitter.Attach(this);
    }

    public SheetSelectionViewModel ViewModel { get; }

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
