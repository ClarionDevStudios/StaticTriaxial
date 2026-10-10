namespace ANX.Protocol.Gr3;

/// <summary>
/// GR-3 协议常量。来源：《综合性能_沥青万能机（GR-3）通讯协议 V1.0》
/// （工作区 `E:\AiStudy\CodeX\docs\gr3-protocol.txt`）第一章。
/// 该协议同时适用于沥青混合料万能试验机与沥青综合性能试验仪。
/// </summary>
public static class Gr3Constants
{
    // ── 外层：UDP 数据包 ─────────────────────────────────────────────
    /// <summary>UDP 帧头（3 字节，0xEE × 3）。</summary>
    public const int UdpSyncLength = 3;

    /// <summary>UDP 帧头 + 数据包长度 = 4 字节。</summary>
    public const int UdpHeaderLength = 4;

    /// <summary>包编号长度（2 字节，高字节在前）。</summary>
    public const int UdpSequenceLength = 2;

    /// <summary>UDP 校验和长度（2 字节，高字节在前）。</summary>
    public const int UdpChecksumLength = 2;

    // ── 内层：指令包 ─────────────────────────────────────────────────
    /// <summary>指令包帧同步符 0xFA 0xFB 0xFC。</summary>
    public const byte InstructionSync0 = 0xFA;

    public const byte InstructionSync1 = 0xFB;

    public const byte InstructionSync2 = 0xFC;

    /// <summary>指令包帧同步符 + 帧长度 = 4 字节。</summary>
    public const int InstructionHeaderLength = 4;

    /// <summary>指令包帧长度的上限（0x9F）。</summary>
    public const int MaxInstructionLength = 0x9F;

    // ── 上行指令（GR-3 → PC）────────────────────────────────────────
    /// <summary>A0H 返回系统配置信息。</summary>
    public const byte UplinkSystemInfo = 0xA0;

    /// <summary>A1H 返回应答。</summary>
    public const byte UplinkAck = 0xA1;

    /// <summary>A2H 返回实时数据（联机后约 120Hz 上传）。</summary>
    public const byte UplinkRealtimeData = 0xA2;

    /// <summary>A3H 模拟通道配置信息应答。</summary>
    public const byte UplinkChannelConfig = 0xA3;

    /// <summary>A4H 返回设备唯一 ID 码。</summary>
    public const byte UplinkDeviceId = 0xA4;

    /// <summary>A1H 应答的状态值：校验失败。</summary>
    public const byte AckChecksumFail = 0;

    /// <summary>A1H 应答的状态值：命令执行成功。</summary>
    public const byte AckSuccess = 1;

    /// <summary>A1H 应答的状态值：无法执行该命令。</summary>
    public const byte AckCannotExecute = 2;

    // ── 下行指令（PC → GR-3）────────────────────────────────────────
    /// <summary>C0H 联机（授时 + 合法性验证）。</summary>
    public const byte DownlinkConnect = 0xC0;

    /// <summary>C1H 设置控制器进入试验模式。</summary>
    public const byte DownlinkTestMode = 0xC1;

    /// <summary>C2H 设置试验编号。</summary>
    public const byte DownlinkTestNumber = 0xC2;

    /// <summary>C3H 设备显示数据清零。</summary>
    public const byte DownlinkZero = 0xC3;

    /// <summary>C4H 控制电机加载。</summary>
    public const byte DownlinkMotorLoad = 0xC4;

    /// <summary>C5H 电机控制命令。</summary>
    public const byte DownlinkMotorControl = 0xC5;

    /// <summary>C6H 试验控制命令。</summary>
    public const byte DownlinkTestControl = 0xC6;

    /// <summary>C7H 设置围压控制命令。</summary>
    public const byte DownlinkConfining = 0xC7;

    /// <summary>C8H 设置温度控制命令。</summary>
    public const byte DownlinkTemperature = 0xC8;

    /// <summary>C9H 修改模拟通道配置。</summary>
    public const byte DownlinkWriteChannelConfig = 0xC9;

    /// <summary>CAH 读取下位机标定 / 设备信息。</summary>
    public const byte DownlinkReadChannelConfig = 0xCA;

    /// <summary>CBH 心跳。</summary>
    public const byte DownlinkHeartbeat = 0xCB;

    // ── 字段长度约定 ─────────────────────────────────────────────────
    /// <summary>设备 ID 长度（24 字节 ASCII）。</summary>
    public const int DeviceIdLength = 24;

    /// <summary>A4H 数据域长度 = ID(24) + 设备类型(2)。</summary>
    public const int DeviceInfoLength = DeviceIdLength + 2;

    /// <summary>试验编号长度（C2H，ASCII 固定 14 字节）。</summary>
    public const int TestNumberLength = 14;
}
