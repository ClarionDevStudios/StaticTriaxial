namespace STS.Communication;

/// <summary>
/// UDP 通道参数。上位机对下位机始终是主动连接方，因此远端地址必填；
/// 本地端口传 0 表示由系统分配（现场若要求固定本地端口，填与远端约定一致的值）。
/// </summary>
/// <param name="RemoteHost">下位机 IP。</param>
/// <param name="RemotePort">下位机端口。</param>
/// <param name="LocalPort">本地绑定端口；0 = 自动。</param>
public sealed record UdpChannelOptions(string RemoteHost, int RemotePort, int LocalPort = 0);
