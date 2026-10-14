namespace ANX.Core.Control;

/// <summary>
/// 网口链路的几种状态。<see cref="MediaDisconnected"/> 与"网卡被禁用"在 .NET 里**都是**
/// <c>OperationalStatus.Down</c>（想区分"网线没插"与"网卡被禁用"得走 WMI 的
/// <c>MSFT_NetAdapter.MediaConnectionState</c>），这里如实按 Down 报，文案里把三种可能都列上。
/// </summary>
public enum NetworkLinkKind
{
    /// <summary>还没检测过。</summary>
    Unknown = 0,

    /// <summary>链路正常（载波在位）。</summary>
    Up = 1,

    /// <summary>网口 Down：网线未插 / 对端（交换机、控制器）掉电 / 网卡被禁用。</summary>
    MediaDisconnected = 2,

    /// <summary>没找到能到达设备的网口（例如网卡被拔掉，或地址段变了）。</summary>
    AdapterMissing = 3,
}

/// <summary>
/// 一次链路检测的结果。界面用 <see cref="Kind"/> 取本地化文案，<see cref="Detail"/> 只进日志
/// （日志本来就写中文，不跟着界面语言走）。
/// </summary>
/// <param name="Kind">状态类别。</param>
/// <param name="AdapterName">承载设备流量的网卡名（如"以太网"）；没定位到时为空串。</param>
/// <param name="Detail">人话解释（中文，进日志）。</param>
public sealed record NetworkLinkState(NetworkLinkKind Kind, string AdapterName, string Detail)
{
    /// <summary>链路是否在位（只有 <see cref="NetworkLinkKind.Up"/> 算在位）。</summary>
    public bool IsUp => Kind == NetworkLinkKind.Up;
}

/// <summary>
/// **网口链路监视**（用户 2026-10-14："能否识别网线的插拔状态"）：
/// 盯住"承载控制器流量的那块网卡"的链路状态，插拔、对端掉电、网卡被禁用都能看出来。
///
/// 为什么需要它：上位机与控制器走的是 **UDP**，UDP 无连接 —— 拔网线**不会**报任何 socket 错误，
/// 收发循环只是"再也没有数据"，所以链路的物理状态必须自己从系统层读，不能等协议报错。
///
/// 与"设备在线"的分工：本监视器只说**网口**通不通（不依赖设备是否响应）；
/// "网口在但控制器不回话"要另外靠协议层心跳判（那是另一条口径，待定）。
/// </summary>
public interface INetworkLinkMonitor
{
    /// <summary>最近一次检测结果。</summary>
    NetworkLinkState State { get; }

    /// <summary>状态**发生变化**时触发（首次检测出结果也算）。</summary>
    event EventHandler<NetworkLinkState>? StateChanged;

    /// <summary>开始监视（幂等）。</summary>
    void Start();

    /// <summary>停止监视（幂等）。</summary>
    void Stop();

    /// <summary>立刻检测一次（<see cref="Start"/> 之后调用；状态没变则什么都不做）。</summary>
    void Check();
}
