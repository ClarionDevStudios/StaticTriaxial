namespace STS.Protocol.Geo;

/// <summary>
/// 下行指令构建器（与现役 `uFunctions.pas` 的 SendC0/C1/C2/C3 **逐字节一致**的布局）。
/// 量纲按现役实测：C1 速度单位 **0.001mm/min**（现役 2400 = 2.4mm/min，调试日志同口径）；
/// C2 目标为 kPa、围压/反压速度单位 0.1mm/min、稳压速度 0.1mm/min、加载杆面积为原始值。
/// </summary>
public static class GeoCommands
{
    public const byte ConnectCommand = 0xC0;

    public const byte MotorCommand = 0xC1;

    public const byte PressureCommand = 0xC2;

    public const byte TestStateCommand = 0xC3;

    public const byte SystemConfigCommand = 0xA0;

    public const byte RealtimeCommand = 0xA1;

    public const byte AckCommand = 0xA2;

    /// <summary>C1 的电机动作。</summary>
    public enum MotorOrder : byte
    {
        Stop = 0,
        Up = 1,
        Down = 2,
    }

    /// <summary>C0 联机：order=1 → 返回一次 A0 后进入联机；order=2 → 联机下主动回送 A1。</summary>
    public static byte[] Connect(byte order) => GeoFrameCodec.Build(ConnectCommand, [0x4F, order]);

    /// <summary>C1 电机：速度单位 0.001mm/min（16 位大端）。</summary>
    public static byte[] Motor(MotorOrder order, int speedRaw)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(speedRaw);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(speedRaw, 0xFFFF);

        return GeoFrameCodec.Build(
            MotorCommand,
            [(byte)order, (byte)(speedRaw >> 8), (byte)speedRaw]
        );
    }

    /// <summary>C2 围压 / 反压（26 字节版，带加载杆面积；4 字节目标值大端、速度 16 位大端）。</summary>
    public static byte[] ConfiningPressure(
        int confiningTargetKPa,
        int backTargetKPa,
        bool startConfining,
        bool startBack,
        int confiningSpeedRaw,
        int backSpeedRaw,
        int stableSpeedRaw,
        uint loadingRodAreaRaw
    )
    {
        var data = new byte[20];
        WriteBigEndian32(data.AsSpan(0), confiningTargetKPa);
        WriteBigEndian32(data.AsSpan(4), backTargetKPa);
        data[8] = (byte)(startConfining ? 1 : 0);
        data[9] = (byte)(startBack ? 1 : 0);
        WriteBigEndian16(data.AsSpan(10), confiningSpeedRaw);
        WriteBigEndian16(data.AsSpan(12), backSpeedRaw);
        WriteBigEndian16(data.AsSpan(14), stableSpeedRaw);
        WriteBigEndian32(data.AsSpan(16), unchecked((int)loadingRodAreaRaw));

        return GeoFrameCodec.Build(PressureCommand, data);
    }

    /// <summary>C3 试验状态：'T' + 动作（0 停止 / 1 试验状态 / 2 饱和过程）。</summary>
    public static byte[] TestState(byte action) =>
        GeoFrameCodec.Build(TestStateCommand, [0x54, action]);

    /// <summary>C3 饱和方式：'S' + 方式（0 抽气 / 1 水头 / 2 反压）。T 1236 用不到，留作 CU/CD 备用。</summary>
    public static byte[] Saturation(byte mode) =>
        GeoFrameCodec.Build(TestStateCommand, [0x53, mode]);

    private static void WriteBigEndian16(Span<byte> target, int value)
    {
        target[0] = (byte)(value >> 8);
        target[1] = (byte)value;
    }

    private static void WriteBigEndian32(Span<byte> target, int value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }
}
