namespace ANX.Protocol.Gr3;

using System.Buffers.Binary;
using System.Text;
using ANX.Core.Protocol;

/// <summary>A1H 应答的状态。</summary>
public enum Gr3AckStatus : byte
{
    /// <summary>校验失败。</summary>
    ChecksumFail = Gr3Constants.AckChecksumFail,

    /// <summary>命令执行成功。</summary>
    Success = Gr3Constants.AckSuccess,

    /// <summary>无法执行该命令。</summary>
    CannotExecute = Gr3Constants.AckCannotExecute,
}

/// <summary>A4H 设备信息。</summary>
public sealed record Gr3DeviceInfo(string Id, ushort DeviceType)
{
    /// <summary>设备类型的中文名（协议文档 2.5 节）。</summary>
    public string DeviceTypeName =>
        DeviceType switch
        {
            0 => "水工版万能机",
            1 => "公路版万能机",
            2 => "伺服版综合",
            3 => "标准版综合",
            4 => "水工版静三轴仪",
            5 => "公路版静三轴仪",
            6 => "弯曲蠕变试验仪",
            _ => $"未知类型({DeviceType})",
        };
}

/// <summary>一个模拟通道的配置（A0H / A3H 共用）。</summary>
/// <param name="Scale">量程 / 满量程码值。</param>
/// <param name="Digital">小数位数。</param>
/// <param name="UnitCode">量纲码，见 <see cref="ANX.Core.Units.UnitCode"/>（1=kN、2=N、3=cm、4=mm）。</param>
public sealed record Gr3ChannelConfig(int Scale, byte Digital, byte UnitCode)
{
    /// <summary>把通道原始码值换算成工程量：raw / 10^Digital。</summary>
    /// <remarks>
    /// 口径沿用玻璃项目 GLT-50 的 A0 <c>Press1Digital</c> 处理（现役同源）。
    /// **待核对**：GR-3 的 Digital 语义要在 M3 接线时用真机读数确认（例如压 0 与压满量程各看一次）。
    /// </remarks>
    public double ToEngineeringValue(int raw) => raw / Math.Pow(10, Digital);
}

/// <summary>A0H 系统配置信息（数据域 56 字节）。</summary>
public sealed record Gr3A0SystemInfo(
    byte HardwareVersion,
    byte FirmwareVersion,
    byte ProtocolVersion,
    IReadOnlyList<Gr3ChannelConfig> PressChannels,
    IReadOnlyList<Gr3ChannelConfig> DisplaceChannels,
    IReadOnlyList<byte> MicrometerValid,
    byte ConfiningPressureValid
);

/// <summary>
/// A2H 的控制器状态寄存器（1 字节）。位定义来自协议文档 2.3 节：
/// Bit0 压力过载 / Bit1 位移上限位 / Bit2 位移下限位 /
/// Bit3 电机正转（上升）运行 / Bit4 电机反转（下降）运行 / Bit5 控温。
/// </summary>
[Flags]
public enum Gr3MachineStatus : byte
{
    None = 0,

    /// <summary>Bit0 压力过载。</summary>
    PressureOverload = 1 << 0,

    /// <summary>Bit1 位移上限位。</summary>
    DisplacementUpperLimit = 1 << 1,

    /// <summary>Bit2 位移下限位。</summary>
    DisplacementLowerLimit = 1 << 2,

    /// <summary>Bit3 电机正转（上升）运行中。</summary>
    MotorForward = 1 << 3,

    /// <summary>Bit4 电机反转（下降）运行中。</summary>
    MotorReverse = 1 << 4,

    /// <summary>Bit5 控温中。</summary>
    TemperatureControlActive = 1 << 5,
}

/// <summary>
/// A2H 的试验状态寄存器（1 字节）。位定义来自协议文档 2.3 节：
/// Bit0 试验运行中 / Bit1 试验暂停中 / Bit2 破坏点已找到 / Bit3 稳压（保持）中
/// （协议文档原文把这一位写作「破碎点」，本工程统一用「破坏」）。
/// </summary>
[Flags]
public enum Gr3TestStatus : byte
{
    None = 0,

    /// <summary>Bit0 试验运行中（0 = 停止）。</summary>
    Running = 1 << 0,

    /// <summary>Bit1 试验暂停中。</summary>
    Paused = 1 << 1,

    /// <summary>Bit2 破坏点已找到（协议原文：破碎点找到标志）。</summary>
    BreakPointFound = 1 << 2,

    /// <summary>Bit3 稳压 / 保持中。</summary>
    Holding = 1 << 3,
}

/// <summary>A2H 实时数据（数据域 71 字节）。</summary>
public sealed record Gr3A2RealtimeData(
    IReadOnlyList<int> PressData,
    int ConfiningData,
    IReadOnlyList<int> DisplaceData,
    IReadOnlyList<int> MicrometerData,
    short TargetTemp,
    short CurrentTemp,
    Gr3MachineStatus MachineStatus,
    Gr3TestStatus TestStatus,
    ushort PortOutputBStatus,
    byte PortInputAStatus,
    ushort PortInputBStatus,
    IReadOnlyList<int> ExtendedExtensometer
)
{
    /// <summary>当前温度（℃，协议规定含 1 位小数）。</summary>
    public double CurrentTemperatureCelsius => CurrentTemp / 10.0;

    /// <summary>目标温度（℃）。</summary>
    public double TargetTemperatureCelsius => TargetTemp / 10.0;
}

/// <summary>A3H 通道配置与标定应答（数据域 95 字节）。</summary>
public sealed record Gr3A3ChannelConfig(
    byte ChannelId,
    Gr3ChannelConfig Config,
    IReadOnlyList<int> CalibrationRealValues,
    IReadOnlyList<int> CalibrationCodeValues
);

/// <summary>
/// 上行指令 → 强类型。所有多字节字段按大端读取（已由真机抓帧验证）。
/// 每个解析器都要求**数据域长度精确匹配**，长度不符即判失败 —— 免得固件改版后悄悄读错字段。
/// </summary>
public static class Gr3UplinkParser
{
    /// <summary>解析 A1H 应答状态。</summary>
    public static bool TryParseAck(DecodedFrame frame, out Gr3AckStatus status)
    {
        status = Gr3AckStatus.ChecksumFail;

        if (frame.CommandCode != Gr3Constants.UplinkAck)
        {
            return false;
        }

        // Status 已由编解码器从数据域首字节取出；未知取值按"校验失败"处理，避免把异常当成功。
        status = frame.Status switch
        {
            Gr3Constants.AckSuccess => Gr3AckStatus.Success,
            Gr3Constants.AckCannotExecute => Gr3AckStatus.CannotExecute,
            _ => Gr3AckStatus.ChecksumFail,
        };

        return true;
    }

    /// <summary>解析 A4H 设备 ID：ID(24 ASCII) + 设备类型(UInt16 大端)。</summary>
    public static bool TryParseDeviceInfo(DecodedFrame frame, out Gr3DeviceInfo info)
    {
        info = new Gr3DeviceInfo(string.Empty, 0);

        if (frame.CommandCode != Gr3Constants.UplinkDeviceId)
        {
            return false;
        }

        var payload = frame.Payload.Span;
        if (payload.Length != Gr3Constants.DeviceInfoLength)
        {
            return false;
        }

        var id = Encoding.ASCII.GetString(payload[..Gr3Constants.DeviceIdLength]);
        var deviceType = BinaryPrimitives.ReadUInt16BigEndian(payload[Gr3Constants.DeviceIdLength..]);

        info = new Gr3DeviceInfo(id, deviceType);
        return true;
    }

    /// <summary>解析 A0H 系统配置：版本(3) + 压力通道量程(4×Int32) + 位移通道量程(4×Int32) + 压力小数位(4) + 位移小数位(4) + 压力量纲(4) + 位移量纲(4) + 微米有效位(4) + 围压有效位(1)。</summary>
    public static bool TryParseSystemInfo(DecodedFrame frame, out Gr3A0SystemInfo info)
    {
        info = null!;

        if (frame.CommandCode != Gr3Constants.UplinkSystemInfo)
        {
            return false;
        }

        var d = frame.Payload.Span;
        if (d.Length != Gr3Constants.SystemInfoLength)
        {
            return false;
        }

        var offset = 0;
        var hardware = d[offset++];
        var firmware = d[offset++];
        var protocol = d[offset++];

        var pressScale = ReadInt32s(d, ref offset, Gr3Constants.PressChannelCount);
        var displaceScale = ReadInt32s(d, ref offset, Gr3Constants.DisplaceChannelCount);
        var pressDigital = ReadBytes(d, ref offset, Gr3Constants.PressChannelCount);
        var displaceDigital = ReadBytes(d, ref offset, Gr3Constants.DisplaceChannelCount);
        var pressUnit = ReadBytes(d, ref offset, Gr3Constants.PressChannelCount);
        var displaceUnit = ReadBytes(d, ref offset, Gr3Constants.DisplaceChannelCount);
        var micrometerValid = ReadBytes(d, ref offset, Gr3Constants.MicrometerChannelCount);
        var confiningValid = d[offset];

        info = new Gr3A0SystemInfo(
            hardware,
            firmware,
            protocol,
            BuildConfigs(pressScale, pressDigital, pressUnit),
            BuildConfigs(displaceScale, displaceDigital, displaceUnit),
            micrometerValid,
            confiningValid
        );

        return true;
    }

    /// <summary>解析 A2H 实时数据（见 <see cref="Gr3A2RealtimeData"/> 的字段顺序）。</summary>
    public static bool TryParseRealtimeData(DecodedFrame frame, out Gr3A2RealtimeData data)
    {
        data = null!;

        if (frame.CommandCode != Gr3Constants.UplinkRealtimeData)
        {
            return false;
        }

        var d = frame.Payload.Span;
        if (d.Length != Gr3Constants.RealtimeDataLength)
        {
            return false;
        }

        var offset = 0;
        var press = ReadInt32s(d, ref offset, Gr3Constants.PressChannelCount);
        var confining = ReadInt32(d, ref offset);
        var displace = ReadInt32s(d, ref offset, Gr3Constants.DisplaceChannelCount);
        var micrometer = ReadInt32s(d, ref offset, Gr3Constants.MicrometerChannelCount);
        var targetTemp = ReadInt16(d, ref offset);
        var currentTemp = ReadInt16(d, ref offset);
        var machineStatus = (Gr3MachineStatus)d[offset++];
        var testStatus = (Gr3TestStatus)d[offset++];
        var portOutputB = ReadUInt16(d, ref offset);
        var portInputA = d[offset++];
        var portInputB = ReadUInt16(d, ref offset);
        var extended = ReadInt32s(d, ref offset, 2);

        data = new Gr3A2RealtimeData(
            press,
            confining,
            displace,
            micrometer,
            targetTemp,
            currentTemp,
            machineStatus,
            testStatus,
            portOutputB,
            portInputA,
            portInputB,
            extended
        );

        return true;
    }

    /// <summary>解析 A3H：通道号(1) + 量程(Int32) + 小数位(1) + 量纲(1) + 标定实值(11×Int32) + 标定码值(11×Int32)。</summary>
    public static bool TryParseChannelConfig(DecodedFrame frame, out Gr3A3ChannelConfig config)
    {
        config = null!;

        if (frame.CommandCode != Gr3Constants.UplinkChannelConfig)
        {
            return false;
        }

        var d = frame.Payload.Span;
        if (d.Length != Gr3Constants.ChannelConfigLength)
        {
            return false;
        }

        var offset = 0;
        var channelId = d[offset++];
        var scale = ReadInt32(d, ref offset);
        var digital = d[offset++];
        var unitCode = d[offset++];
        var realValues = ReadInt32s(d, ref offset, Gr3Constants.CalibrationPointCount);
        var codeValues = ReadInt32s(d, ref offset, Gr3Constants.CalibrationPointCount);

        config = new Gr3A3ChannelConfig(
            channelId,
            new Gr3ChannelConfig(scale, digital, unitCode),
            realValues,
            codeValues
        );

        return true;
    }

    /// <summary>构造 A4H 数据域（测试与模拟器用）。</summary>
    public static byte[] BuildDeviceInfoPayload(string deviceId, ushort deviceType)
    {
        var idBytes = new byte[Gr3Constants.DeviceIdLength];
        var source = Encoding.ASCII.GetBytes(deviceId);
        var copyLength = Math.Min(source.Length, Gr3Constants.DeviceIdLength);
        for (var i = 0; i < Gr3Constants.DeviceIdLength; i++)
        {
            idBytes[i] = i < copyLength ? source[i] : (byte)'0';
        }

        return [.. idBytes, (byte)(deviceType >> 8), (byte)(deviceType & 0xFF)];
    }

    private static IReadOnlyList<Gr3ChannelConfig> BuildConfigs(
        IReadOnlyList<int> scales,
        IReadOnlyList<byte> digits,
        IReadOnlyList<byte> units
    )
    {
        var result = new Gr3ChannelConfig[scales.Count];
        for (var i = 0; i < scales.Count; i++)
        {
            result[i] = new Gr3ChannelConfig(scales[i], digits[i], units[i]);
        }

        return result;
    }

    private static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        var result = data.Slice(offset, count).ToArray();
        offset += count;
        return result;
    }

    private static int[] ReadInt32s(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = ReadInt32(data, ref offset);
        }

        return result;
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
        offset += 4;
        return value;
    }
}
