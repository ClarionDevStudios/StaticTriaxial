namespace STS.Core.Communication;

/// <summary>
/// 通讯通道种类。定档只保留三种：串口 / UDP / 模拟器。
/// 模拟器仅 Debug 构建使用（Release 编译期排除），接口与枚举保持稳定。
/// </summary>
public enum ChannelKind
{
    /// <summary>UDP（本机型主用通道）。</summary>
    Udp = 0,

    /// <summary>串口（RS232 / USB 转串口）。</summary>
    Serial = 1,

    /// <summary>模拟器（开发期无真机联调用）。</summary>
    Simulator = 2,
}
