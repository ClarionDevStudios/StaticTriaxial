namespace ANX.Ui.ViewModels;

using ANX.Ui.Services;
using Wpf.Ui.Controls;

/// <summary>
/// 外壳 ViewModel：重建导航项与标题。
/// 也是 <see cref="INavigationPaneService"/> 的实现者 —— 导航栏开合状态由这里持有
/// （`ShellWindow` 的 `NavigationView.IsPaneOpen` 双向绑到 <see cref="IsPaneOpen"/>）。
/// </summary>
public partial class ShellViewModel : ViewModelBase, INavigationPaneService
{
    [ObservableProperty]
    private string _applicationTitle = string.Empty;

    [ObservableProperty]
    private ObservableCollection<object> _navigationItems = [];

    [ObservableProperty]
    private ObservableCollection<object> _navigationFooter = [];

    /// <summary>
    /// 导航栏是否展开（与外壳里的 `NavigationView.IsPaneOpen` 双向绑定）。
    /// 折叠时项上只剩图标，此时才给每项挂文字提示（用户 2026-10-13）。
    /// </summary>
    [ObservableProperty]
    private bool _isPaneOpen = true;

    public ShellViewModel(ILocalizationService localization) : base(localization)
    {
        localization.LanguageChanged += (_, _) => RebuildNavigation();
        RebuildNavigation();
    }

    private void RebuildNavigation()
    {
        ApplicationTitle = L("App.Title");

        NavigationItems =
        [
            new NavigationViewItem
            {
                Content = L("Nav.Execution"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.Beaker24 },
                TargetPageType = typeof(Views.Pages.TestExecutionPage),
            },
            new NavigationViewItem
            {
                Content = L("Nav.History"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.History24 },
                TargetPageType = typeof(Views.Pages.HistoryPage),
            },
            new NavigationViewItem
            {
                Content = L("Nav.Home"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
                TargetPageType = typeof(Views.Pages.HomePage),
            },
        ];

        NavigationFooter =
        [
            new NavigationViewItem
            {
                Content = L("Nav.Settings"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
                TargetPageType = typeof(Views.Pages.SettingsPage),
            },
            new NavigationViewItem
            {
                Content = L("Nav.Exit"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.Power24 },
                Command = ExitCommand,
            },
        ];

        UpdateNavigationToolTips();
    }

    partial void OnIsPaneOpenChanged(bool value) => UpdateNavigationToolTips();

    /// <inheritdoc />
    /// <remarks>
    /// 进试验主界面就收起导航栏（用户 2026-10-14）—— 那一页控件多、要宽度；
    /// 收起后项上只剩图标，悬停提示由 <see cref="UpdateNavigationToolTips"/> 自动挂上。
    /// </remarks>
    public void Collapse() => IsPaneOpen = false;

    /// <summary>
    /// **折叠时**给每个导航项挂上文字提示（用户 2026-10-13：折叠后鼠标悬停要能看到该项是什么）。
    /// 展开时项上本来就有文字，提示是多余的，所以置空 —— 提示只在"看不出来"的时候出现。
    /// 文案直接取项的 <see cref="NavigationViewItem.Content"/>（也就是当前语言的导航名），
    /// 换语言后 <see cref="RebuildNavigation"/> 会重建项并重新挂一遍。
    /// </summary>
    private void UpdateNavigationToolTips()
    {
        foreach (
            var item in NavigationItems
                .Concat(NavigationFooter)
                .OfType<NavigationViewItem>()
        )
        {
            item.ToolTip = IsPaneOpen ? null : item.Content;
        }
    }

    /// <summary>
    /// 问一句"确定要退出软件吗？"。**导航栏「退出系统」与主窗口右上角的 X 共用这一句**
    /// （用户 2026-10-15：X 直接关掉、没有提示），所以文案与对话框尺寸只有这一处。
    /// </summary>
    /// <returns><c>true</c> = 用户确认退出。</returns>
    public Task<bool> ConfirmExitAsync() =>
        AppDialogs.ConfirmAsync(
            L("Nav.Exit"),
            L("Common.ExitConfirm"),
            L("Common.Yes"),
            L("Common.No")
        );

    /// <summary>
    /// 导航栏「退出系统」：**关主窗口**，退出确认交给窗口自己（与右上角的 X 完全同一条路）。
    ///
    /// 这里刻意不问、也不退：两条入口各问一遍必然分叉 —— 用户 2026-10-15 实测到两处不一致
    /// （导航栏点"取消"会跳到系统设置，X 点"取消"留在原页）。顺带说明为什么不再"还原选中项"：
    /// WPF-UI 的 <c>NavigationViewItem.OnClick</c> 只在**带 TargetPageType** 的项上触发导航/选中，
    /// 这一项只有 Command，点了既不改选中也不改高亮（翻 4.3.0 源码确认），所以那句导航是多余的。
    /// </summary>
    [RelayCommand]
    private void Exit() => Application.Current?.MainWindow?.Close();
}
