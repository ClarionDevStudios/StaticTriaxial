namespace ANX.Ui.Services;

/// <summary>
/// 退出确认的**闸门**：主窗口右上角的 X 与导航栏「退出系统」共用同一个确认框，
/// 谁先问了就把闸门立起来，另一处不再问第二遍；程序自己正在退（未处理异常选了退出、
/// 注销关机）时也立起来 —— 否则关窗会拦在退出流程中间弹框（用户 2026-10-15：X 直接关掉、没有提示）。
///
/// 用静态是因为**宿主层（App）也要能立它**：`App` 在异常提示框里问过"是否退出"之后直接
/// `Shutdown(1)`，那时没有窗口实例可以传。
/// </summary>
public static class ExitGuard
{
    /// <summary>已经确认过退出（或程序正在自己退）：关主窗口不再弹确认。</summary>
    public static bool IsAllowed { get; private set; }

    /// <summary>立起闸门。</summary>
    public static void AllowExit() => IsAllowed = true;
}
