namespace ANX.Ui.Views;

using System.ComponentModel;
using System.Windows.Threading;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

/// <summary>外壳窗口。导航走 WPF-UI 的内建机制（TargetPageType + INavigationService）。</summary>
public partial class ShellWindow : INavigationWindow
{
    /// <summary>正在问"要不要退出"（连点两下 X 不要弹两个框）。</summary>
    private bool _confirmingExit;

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

    /// <summary>
    /// 点标题栏右上角的 X 时先问一句"确定要退出软件吗？"（用户 2026-10-15：原先直接关掉、没有提示）。
    ///
    /// 与导航栏「退出系统」共用 <see cref="ShellViewModel.ConfirmExitAsync"/>，文案与尺寸只有一处；
    /// 谁先问过谁就立起 <see cref="ExitGuard"/>，另一处不再问第二遍。
    ///
    /// 确认框是**异步**的（WPF-UI 的 MessageBox 只有 `ShowDialogAsync`），所以这里先 `e.Cancel`
    /// 拦下这次关闭，等用户答完"是"再自己关一次 —— 那一次闸门已立，走上面那条直接放行的分支。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (ExitGuard.IsAllowed || _confirmingExit)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        _ = ConfirmExitAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current?.Shutdown();
    }

    private async Task ConfirmExitAsync()
    {
        _confirmingExit = true;

        try
        {
            if (!await ViewModel.ConfirmExitAsync())
            {
                return;
            }

            ExitGuard.AllowExit();

            // **不能直接 Close()**：WPF-UI 的 MessageBox 是"先 TrySetResult 再关自己"，而默认的
            // TaskCompletionSource 会让 await 的后续**同步**跑在它 OnButtonClick 里 —— 也就是说
            // 这一行执行时，模态框还没关完、主窗口还处在"关闭中"状态，Close() 会抛
            // 「在窗口关闭期间，无法将可见性设置为可见，也无法调用 Show、ShowDialog、Close…」
            // 而被 `_ =` 丢掉（用户 2026-10-15 实测：点"是"框关了、软件不退；再点一次 X 才退）。
            // 把关窗推到消息队列**空下来之后**（ApplicationIdle）：那时模态帧已收干净、主窗口已恢复，
            // 实测能正常关闭（探针两个场景对比过：直接 Close() 抛异常不生效，这样关 closed=True）。
            await Dispatcher.InvokeAsync(Close, DispatcherPriority.ApplicationIdle);
        }
        finally
        {
            _confirmingExit = false;
        }
    }
}
