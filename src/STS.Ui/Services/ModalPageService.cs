namespace STS.Ui.Services;

using System.Windows;
using System.Windows.Controls;

/// <summary>
/// **弹窗里的 ViewModel** 用它把结果交给宿主窗体：
/// <c>true</c> = 「确定」（宿主据此刷新，例如试验单换了一张），<c>false</c> = 「取消」/ 关窗。
/// </summary>
public interface IModalDialogViewModel
{
    event EventHandler<bool>? CloseRequested;
}

/// <summary>
/// **可以作为弹窗打开的页面**：把 ViewModel 的关窗请求转发出来。
/// 页面在构造时把 <c>DataContext</c> 设成了自己（不是 ViewModel），宿主拿不到 ViewModel，
/// 所以由页面转一手（页面实现本接口，事件直接转发给 <see cref="IModalDialogViewModel"/>）。
/// </summary>
public interface IModalPage
{
    event EventHandler<bool>? CloseRequested;

    /// <summary>
    /// 弹窗弹出时调用 —— 等价于导航页面的 <c>OnNavigatedTo</c>：把当前值读一遍
    /// （页面与 ViewModel 都是单例，构造函数只跑一次，不能靠它刷新）。
    /// </summary>
    void OnDialogOpened();
}

/// <summary>
/// 把**页面**当弹出窗体打开（用户 2026-10-13：试验参数页、编辑试验信息页改成弹窗）。
/// 页面本身不改结构 —— 还是那个 <see cref="Page"/>，只是换个宿主装进模态窗口里。
/// </summary>
public interface IModalPageService
{
    /// <summary>模态打开一个页面；返回 true = 页面给了「确定」结果（关窗 / 取消回 false）。</summary>
    bool Show(Type pageType, double width, double height);
}

/// <summary>
/// 默认实现：从 DI 取页面 → 装进 <see cref="Views.Dialogs.PageDialogWindow"/> → <c>ShowDialog()</c>。
/// </summary>
public sealed class ModalPageService(IServiceProvider services) : IModalPageService
{
    /// <inheritdoc />
    public bool Show(Type pageType, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        if (services.GetService(pageType) is not Page page)
        {
            throw new InvalidOperationException(
                $"页面 {pageType.Name} 没有注册到容器，无法作为弹窗打开"
            );
        }

        var owner = Application.Current?.MainWindow;

        // 窗体尺寸**不许超出屏幕工作区**（用户 2026-10-15 报"预览窗体的高度要调整"）：
        // 本机是 1920×1080 但**缩放 125%** → 工作区只有 1536×816 DIP，而预览窗写死 900 DIP 高，
        // 物理上 1125px 比工作区 1020px 还高，底边那条会被裁掉（弹窗是 NoResize，用户也没法拉）。
        // 以后所有弹窗都从工作区里留 24 DIP 边距来夹一下，屏幕小/缩放大的机器上都不会再出这问题。
        var workArea = System.Windows.SystemParameters.WorkArea;

        var window = new Views.Dialogs.PageDialogWindow
        {
            Width = Math.Min(width, workArea.Width - 24),
            Height = Math.Min(height, workArea.Height - 24),
            Owner = owner,
            WindowStartupLocation =
                owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
        };

        window.Host(page);

        // 关窗按钮（标题栏 X）不会给 DialogResult，ShowDialog 回 null → 当作「取消」处理
        return window.ShowDialog() == true;
    }
}
