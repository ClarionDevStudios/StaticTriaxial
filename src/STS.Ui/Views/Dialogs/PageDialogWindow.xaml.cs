namespace STS.Ui.Views.Dialogs;

using System.Windows.Controls;
using STS.Ui.Services;
using Wpf.Ui.Controls;

/// <summary>
/// 弹窗宿主：把一个**页面**装进模态窗口（用户 2026-10-13）。
/// 页面照旧通过自己的 ViewModel 干活，只是不再靠导航进出 —— 需要关窗时由 ViewModel 发
/// <see cref="IModalDialogViewModel.CloseRequested"/>，这里转成 <c>DialogResult</c> 关掉窗口。
/// </summary>
public partial class PageDialogWindow : FluentWindow
{
    /// <summary>
    /// 装进来的页面。**必须自己记住**：ViewModel 是单例，关闭时事件里带的 <c>sender</c> 是
    /// ViewModel（不是页面），靠它退订是退不掉的（页面只是把订阅转发给 ViewModel）。
    /// </summary>
    private IModalPage? _page;

    public PageDialogWindow()
    {
        InitializeComponent();

        // 标题栏 X 关窗也要退订：不退订的话处理器会留在单例 ViewModel 上，
        // 下一次弹窗关闭时旧窗口的处理器又跑一次 —— 对**已关闭**的窗口设 DialogResult
        // 会抛 "只能在创建 Window 并且作为对话框显示之后才能设置 DialogResult"（用户 2026-10-13 实测）。
        Closed += (_, _) => Detach();
    }

    /// <summary>把页面装进窗口，并接上它的关窗请求。</summary>
    public void Host(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Body.Content = page;

        if (page is IModalPage modalPage)
        {
            _page = modalPage;
            modalPage.CloseRequested += OnCloseRequested;
            modalPage.OnDialogOpened(); // 弹出自带"读一遍当前值"，与导航页的 OnNavigatedTo 等价
        }
    }

    /// <summary>退订页面的关窗请求（幂等：重复调用没副作用）。</summary>
    private void Detach()
    {
        if (_page is null)
        {
            return;
        }

        _page.CloseRequested -= OnCloseRequested;
        _page = null;
    }

    private void OnCloseRequested(object? sender, bool result)
    {
        Detach();

        // 兜底：窗口已经不是"作为对话框显示中"的状态就不设 DialogResult（设了会抛）
        if (!IsVisible)
        {
            return;
        }

        // 设 DialogResult 会顺带关窗；ShowDialog 的返回值就是宿主判断"确定/取消"的依据
        DialogResult = result;
    }
}
