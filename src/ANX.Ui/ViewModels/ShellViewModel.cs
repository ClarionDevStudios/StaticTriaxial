namespace ANX.Ui.ViewModels;

using ANX.Ui.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

/// <summary>
/// 外壳 ViewModel：重建导航项与标题。
/// 也是 <see cref="INavigationPaneService"/> 的实现者 —— 导航栏开合状态由这里持有
/// （`ShellWindow` 的 `NavigationView.IsPaneOpen` 双向绑到 <see cref="IsPaneOpen"/>）。
/// </summary>
public partial class ShellViewModel : ViewModelBase, INavigationPaneService
{
    private readonly INavigationService _navigationService;

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

    public ShellViewModel(ILocalizationService localization, INavigationService navigationService)
        : base(localization)
    {
        _navigationService = navigationService;

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

    /// <summary>导航栏「退出系统」：问一句，确认了就退出程序。</summary>
    [RelayCommand]
    private async Task ExitAsync()
    {
        if (!await ConfirmExitAsync())
        {
            // 取消后把选中项还原，避免"退出"这一项一直高亮。
            _navigationService.Navigate(typeof(Views.Pages.SettingsPage));
            return;
        }

        // 已经问过了：立闸门再退，免得主窗口在退出流程里又弹一遍确认
        ExitGuard.AllowExit();
        Application.Current?.Shutdown();
    }
}
