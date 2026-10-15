namespace ANX.Protocol.Gr3;

/// <summary>
/// GR-3 协议层抛给界面的**消息资源键**（联机/应答超时、设备拒命令）——
/// 这些会出现在操控信息里，语言切换也要盖住。协议层不认识语言：带键 + 中文兜底。
/// </summary>
public static class ProtocolMessageKeys
{
    /// <summary>联机超时：{0} = 超时秒数。</summary>
    public const string ConnectTimeout = "Msg.Gr3.ConnectTimeout";

    /// <summary>等待 A1 应答超时：{0} = 命令码（十六进制）。</summary>
    public const string AckTimeout = "Msg.Gr3.AckTimeout";

    /// <summary>设备拒绝命令：{0} = 命令码、{1} = 状态码。</summary>
    public const string CommandRejected = "Msg.Gr3.CommandRejected";

    /// <summary>全部键 —— 供语言字典一致性用例把守。</summary>
    public static readonly string[] All = [ConnectTimeout, AckTimeout, CommandRejected];
}
