namespace STS.Protocol.Geo;

/// <summary>
/// 土工（V1.7）协议使用的 CRC-8。
///
/// 参数与现役 Delphi `uCrc8Code.pas` 的 `CRCTable` 一致（本机实测：运行期生成的表与现役字面量表
/// **256 项逐项相等**，见 `GeoCrc8Tests`）：多项式 `0xD5`（x⁸+x⁷+x⁶+x⁴+x²+1）、
/// 初值 `0x00`、MSB first、不反转、结果不异或。
///
/// 校验范围：从**帧长度字节**开始，到有效数据结束（不含帧头，也不含 CRC 自身）——
/// 与现役 `GenerateCRC8CodeByCmdArray`（从下标 3 起、到 `Length - 2` 止）同口径。
/// </summary>
public static class GeoCrc8
{
    private const byte Polynomial = 0xD5;

    private static readonly byte[] Table = BuildTable();

    private static byte[] BuildTable()
    {
        var table = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var crc = (byte)i;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80) != 0
                    ? (byte)((crc << 1) ^ Polynomial)
                    : (byte)(crc << 1);
            }

            table[i] = crc;
        }

        return table;
    }

    /// <summary>查表结果，供单元测试与现役字面量表逐项比对。</summary>
    public static ReadOnlySpan<byte> LookupTable => Table;

    public static byte Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0;

        foreach (var b in data)
        {
            crc = Table[crc ^ b];
        }

        return (byte)crc;
    }
}
