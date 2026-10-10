namespace ANX.Protocol.Gr3;

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

/// <summary>上行指令 → 强类型。M2 只做联机握手用到的 A1 / A4；A0 / A2 / A3 随 M3 落地。</summary>
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

        // Status 已由编解码器从数据域首字节取出；未知取值按"无法执行"处理，避免把异常当成功。
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
        var deviceType = (ushort)((payload[Gr3Constants.DeviceIdLength] << 8) | payload[Gr3Constants.DeviceIdLength + 1]);

        info = new Gr3DeviceInfo(id, deviceType);
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

        return
        [
            .. idBytes,
            (byte)(deviceType >> 8),
            (byte)(deviceType & 0xFF),
        ];
    }
}
