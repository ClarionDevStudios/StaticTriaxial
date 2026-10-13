namespace ANX.Ui.ViewModels;

using ANX.Ui.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

/// <summary>外壳 ViewModel：重建导航项与标题。</summary>
public partial class ShellViewModel : ViewModelBase
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

    [RelayCommand]
    private async Task ExitAsync()
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = L("Nav.Exit"),
            Content = L("Common.ExitConfirm"),
            PrimaryButtonText = L("Common.Yes"),
            SecondaryButtonText = L("Common.No"),
            IsCloseButtonEnabled = false,
            Owner = Application.Current?.MainWindow,
        };

        var result = await messageBox.ShowDialogAsync();

        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            Application.Current?.Shutdown();
            return;
        }

        // 取消后把选中项还原，避免"退出"这一项一直高亮。
        _navigationService.Navigate(typeof(Views.Pages.SettingsPage));
    }
}
