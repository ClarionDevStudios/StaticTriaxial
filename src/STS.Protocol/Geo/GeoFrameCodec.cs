namespace STS.Protocol.Geo;

/// <summary>一帧（已通过 CRC 校验）。<paramref name="Data"/> = 指令符之后的"有效数据"。</summary>
public sealed record GeoFrame(byte Command, byte[] Data);

/// <summary>解帧结果。</summary>
public enum GeoDecodeStatus
{
    /// <summary>解出一帧；<c>consumed</c> = 该帧占用的字节数。</summary>
    Ok = 0,

    /// <summary>数据不足，等更多字节（<c>consumed</c> = 0）。</summary>
    NeedMoreData = 1,

    /// <summary>不是帧头 / 长度非法 / CRC 不符；调用方应跳过 1 字节重同步（<c>consumed</c> = 1）。</summary>
    Invalid = 2,
}

/// <summary>
/// 土工协议帧编解码（V1.7 通用帧）：
/// `FA FB FC` + 帧长度（1B；= 指令符 1 + 有效数据 N + CRC 1，即 N + 2）
/// + 指令符（1B；A0-BF 上行 / C0-DF 下行）+ 有效数据（≤256B）+ CRC-8（1B）。
///
/// 与现役逐字节构建（`uFunctions.pas` 的 SendC0/C1/C2/C3）同布局：
/// C0 = `FA FB FC 04 C0 'O' ORDER CRC`；C1 = `FA FB FC 05 C1 ORDER SpeedHi SpeedLo CRC`；
/// C2 = `FA FB FC 16 C2 …`（26 字节版）；C3 = `FA FB FC 04 C3 'T' ORDER CRC`。
/// </summary>
public static class GeoFrameCodec
{
    public const byte Head0 = 0xFA;
    public const byte Head1 = 0xFB;
    public const byte Head2 = 0xFC;

    /// <summary>组一帧（自动补帧头、帧长度与 CRC）。</summary>
    public static byte[] Build(byte command, ReadOnlySpan<byte> data)
    {
        // 帧总长 = 帧头 3 + 长度 1 + 指令符 1 + 数据 N + CRC 1
        var frame = new byte[data.Length + 6];
        frame[0] = Head0;
        frame[1] = Head1;
        frame[2] = Head2;
        frame[3] = (byte)(data.Length + 2);
        frame[4] = command;
        data.CopyTo(frame.AsSpan(5));
        frame[^1] = GeoCrc8.Compute(frame.AsSpan(3, data.Length + 2));

        return frame;
    }

    /// <summary>从缓冲区头部解一帧（流式：数据不足返回 <see cref="GeoDecodeStatus.NeedMoreData"/>）。</summary>
    public static GeoDecodeStatus TryDecode(
        ReadOnlySpan<byte> buffer,
        out GeoFrame? frame,
        out int consumed
    )
    {
        frame = null;
        consumed = 0;

        if (buffer.Length < 4)
        {
            return GeoDecodeStatus.NeedMoreData;
        }

        if (buffer[0] != Head0 || buffer[1] != Head1 || buffer[2] != Head2)
        {
            consumed = 1;
            return GeoDecodeStatus.Invalid;
        }

        var length = buffer[3];

        // 长度至少含"指令符 + CRC"两个字节
        if (length < 2)
        {
            consumed = 1;
            return GeoDecodeStatus.Invalid;
        }

        var total = length + 4;

        if (buffer.Length < total)
        {
            return GeoDecodeStatus.NeedMoreData;
        }

        var expected = GeoCrc8.Compute(buffer.Slice(3, length));

        if (expected != buffer[total - 1])
        {
            consumed = 1;
            return GeoDecodeStatus.Invalid;
        }

        frame = new GeoFrame(buffer[4], buffer.Slice(5, length - 2).ToArray());
        consumed = total;

        return GeoDecodeStatus.Ok;
    }
}
