namespace ANX.Ui.Views;

using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

/// <summary>外壳窗口。导航走 WPF-UI 的内建机制（TargetPageType + INavigationService）。</summary>
public partial class ShellWindow : INavigationWindow
{
    public ShellWindow(
        ShellViewModel viewModel,
        INavigationService navigationService,
        IServiceProvider serviceProvider
    )
    {
        // 先拿到 ViewModel 再 InitializeComponent：不要依赖 DataContextChanged（它只在赋值之后订阅就永不触发）。
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        navigationService.SetNavigationControl(RootNavigation);
        SetServiceProvider(serviceProvider);
    }

    public ShellViewModel ViewModel { get; }

    public INavigationView GetNavigation() => RootNavigation;

    public bool Navigate(Type pageType) => RootNavigation.Navigate(pageType);

    public void SetServiceProvider(IServiceProvider serviceProvider) =>
        RootNavigation.SetServiceProvider(serviceProvider);

    public void SetPageService(INavigationViewPageProvider navigationViewPageProvider) =>
        RootNavigation.SetPageProviderService(navigationViewPageProvider);

    public void ShowWindow() => Show();

    public void CloseWindow() => Close();

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current?.Shutdown();
    }
}
