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
                Content = L("Nav.Sheet"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.DocumentTable24 },
                TargetPageType = typeof(Views.Pages.SheetSelectionPage),
            },
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
                Content = L("Nav.SampleEditor"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.Ruler24 },
                TargetPageType = typeof(Views.Pages.SampleEditorPage),
            },
            new NavigationViewItem
            {
                Content = L("Nav.Machine"),
                Icon = new SymbolIcon { Symbol = SymbolRegular.SettingsCogMultiple24 },
                TargetPageType = typeof(Views.Pages.MachineSettingsPage),
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
