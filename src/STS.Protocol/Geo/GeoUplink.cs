namespace STS.Protocol.Geo;

using System.Buffers.Binary;

/// <summary>
/// A0 系统配置（按 V1.7 第 5.5 节字段表解析）。
/// 版本号"包含一位小数"（原始值 /10，如 15 → 1.5）；量程/小数位/单位为原始值（含义由现场配置约定）。
/// </summary>
public sealed record GeoSystemConfig(
    double HardwareVersion,
    double FirmwareVersion,
    double ProtocolVersion,
    int Press1Scale,
    int Press2Scale,
    int Displacement1Scale,
    int Displacement2Scale,
    int Press1Digits,
    int Press2Digits,
    int Displacement1Digits,
    int Displacement2Digits,
    byte Press1Unit,
    byte Press2Unit,
    byte Displacement1Unit,
    byte Displacement2Unit,
    bool Displacement3Valid,
    bool Displacement4Valid,
    bool Displacement5Valid,
    bool Displacement6Valid,
    bool Press3Valid,
    bool Press4Valid,
    bool Press5Valid
)
{
    /// <summary>解析 A0（至少要有前 27 字节：版本 3 + 量程 16 + 小数位 4 + 单位 4）。</summary>
    public static bool TryParse(GeoFrame frame, out GeoSystemConfig? config)
    {
        config = null;

        if (frame.Command != GeoCommands.SystemConfigCommand || frame.Data.Length < 27)
        {
            return false;
        }

        var data = frame.Data;

        config = new GeoSystemConfig(
            HardwareVersion: data[0] / 10.0,
            FirmwareVersion: data[1] / 10.0,
            ProtocolVersion: data[2] / 10.0,
            Press1Scale: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(3)),
            Press2Scale: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(7)),
            Displacement1Scale: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(11)),
            Displacement2Scale: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(15)),
            Press1Digits: data[19],
            Press2Digits: data[20],
            Displacement1Digits: data[21],
            Displacement2Digits: data[22],
            Press1Unit: data[23],
            Press2Unit: data[24],
            Displacement1Unit: data[25],
            Displacement2Unit: data[26],
            Displacement3Valid: data.Length >= 28 && data[27] != 0,
            Displacement4Valid: data.Length >= 29 && data[28] != 0,
            Displacement5Valid: data.Length >= 30 && data[29] != 0,
            Displacement6Valid: data.Length >= 31 && data[30] != 0,
            Press3Valid: data.Length >= 32 && data[31] != 0,
            Press4Valid: data.Length >= 33 && data[32] != 0,
            Press5Valid: data.Length >= 34 && data[33] != 0
        );

        return true;
    }
}

/// <summary>
/// A1 实时数据（按 V1.7 第 5.6 节字段表解析）。
/// 固定单位：Displacement3-6 = 0.001mm；PRESS3/4/5（围压/反压/孔压）= 0.001MPa；
/// 四个体积/排水量 = 0.001ml（V1.7 新增，老固件可能没有——长度不足时按 0 处理）。
/// PRESS1/2 与 Displacement1/2 的换算依赖 A0 的小数位/量程，保留原始值。
/// </summary>
public sealed record GeoRealtimeSample(
    int Press1Raw,
    int Press2Raw,
    int Displacement1Raw,
    int Displacement2Raw,
    short Displacement3Raw,
    short Displacement4Raw,
    short Displacement5Raw,
    short Displacement6Raw,
    short Press3Raw,
    short Press4Raw,
    short Press5Raw,
    uint PressureSystemStatus,
    byte MainStatus,
    byte TestStatus,
    int ConfiningVolumeRaw,
    int BackVolumeRaw,
    int MasterVolumeRaw,
    int DrainVolumeRaw,
    int Data1Raw,
    int Data2Raw
)
{
    /// <summary>是否带 V1.7 的体积四字段（老固件帧短，字段按 0 处理）。</summary>
    public bool HasVolumes { get; init; }

    /// <summary>围压（MPa；原始值单位 0.001MPa，即数值 = kPa/1000）。</summary>
    public double ConfiningPressureMPa => Press3Raw * 0.001;

    public double BackPressureMPa => Press4Raw * 0.001;

    public double PorePressureMPa => Press5Raw * 0.001;

    /// <summary>主机电机正转（上升）运行（Main Status Bit3）。</summary>
    public bool MotorUp => (MainStatus & 0x08) != 0;

    /// <summary>主机电机反转（下降）运行（Main Status Bit4）。</summary>
    public bool MotorDown => (MainStatus & 0x10) != 0;

    /// <summary>试验运行状态（Test Status Bit0）。</summary>
    public bool TestRunning => (TestStatus & 0x01) != 0;

    /// <summary>围压稳压状态（SATAE Bit10）/ 反压稳压（Bit11）。</summary>
    public bool ConfiningStable => (PressureSystemStatus & (1u << 10)) != 0;

    public bool BackStable => (PressureSystemStatus & (1u << 11)) != 0;

    /// <summary>排水量（ml；原始值单位 0.001ml）。T 1236 不要求入库，先解析备用。</summary>
    public double DrainVolumeML => DrainVolumeRaw * 0.001;

    public double ConfiningVolumeML => ConfiningVolumeRaw * 0.001;

    public double BackVolumeML => BackVolumeRaw * 0.001;

    public double MasterVolumeML => MasterVolumeRaw * 0.001;

    /// <summary>解析 A1（前 36 字节：压力/位移/围压/状态；体积四字段与预留字段按长度可选）。</summary>
    public static bool TryParse(GeoFrame frame, out GeoRealtimeSample? sample)
    {
        const int MinimumLength = 36;
        const int WithVolumesLength = 52;
        const int FullLength = 60;

        sample = null;

        if (frame.Command != GeoCommands.RealtimeCommand || frame.Data.Length < MinimumLength)
        {
            return false;
        }

        var data = frame.Data;
        var hasVolumes = data.Length >= WithVolumesLength;

        sample = new GeoRealtimeSample(
            Press1Raw: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0)),
            Press2Raw: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4)),
            Displacement1Raw: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(8)),
            Displacement2Raw: BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(12)),
            Displacement3Raw: BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(16)),
            Displacement4Raw: BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(18)),
            Displacement5Raw: BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(20)),
            Displacement6Raw: BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(22)),
            Press3Raw: BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(24)),
            Press4Raw: BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(26)),
            Press5Raw: BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(28)),
            PressureSystemStatus: BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(30)),
            MainStatus: data[34],
            TestStatus: data[35],
            ConfiningVolumeRaw: hasVolumes
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(36))
                : 0,
            BackVolumeRaw: hasVolumes
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(40))
                : 0,
            MasterVolumeRaw: hasVolumes
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(44))
                : 0,
            DrainVolumeRaw: hasVolumes
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(48))
                : 0,
            Data1Raw: data.Length >= FullLength
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(52))
                : 0,
            Data2Raw: data.Length >= FullLength
                ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(56))
                : 0
        )
        {
            HasVolumes = hasVolumes,
        };

        return true;
    }
}

/// <summary>A2 应答状态（V1.7 第 5.7 节）。</summary>
public enum GeoAckStatus : byte
{
    CrcError = 0,

    Success = 1,

    CannotExecute = 2,
}

/// <summary>A2 应答。</summary>
public sealed record GeoAck(GeoAckStatus Status)
{
    public static bool TryParse(GeoFrame frame, out GeoAck? ack)
    {
        ack = null;

        if (frame.Command != GeoCommands.AckCommand || frame.Data.Length < 1)
        {
            return false;
        }

        ack = new GeoAck((GeoAckStatus)frame.Data[0]);

        return true;
    }
}
