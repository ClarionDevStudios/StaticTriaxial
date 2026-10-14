namespace ANX.Ui.Services;

/// <summary>
/// 左侧导航栏（面板）的开合控制。
///
/// **为什么要这个**：面板状态归外壳所有（`ShellWindow` 里的 `NavigationView.IsPaneOpen`
/// 双向绑定在 <c>ShellViewModel</c> 上），而"该不该收起"是**页面**知道的事
/// （用户 2026-10-14：进试验主界面后自动收缩）。页面不该去摸外壳的控件，
/// 所以由外壳实现这个服务、页面通过它请求收起。
/// </summary>
public interface INavigationPaneService
{
    /// <summary>收起左侧导航栏（只影响面板开合，不影响当前页）。</summary>
    void Collapse();
}
