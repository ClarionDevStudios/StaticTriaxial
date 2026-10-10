namespace ANX.Protocol.Gr3;

/// <summary>
/// CRC-8，多项式 0xD5（x⁸+x⁷+x⁶+x⁴+x²+1），初值 0x00、非反射、结果不异或。
/// 已与现役 Delphi `UdpClientEx.pas` 的 <c>CRCTable</c> 字面量逐项核对一致（见 ANX.Protocol.Tests）。
/// </summary>
public static class Gr3Crc8
{
    /// <summary>256 项查表。公开出来是为了让测试能逐项对照现役字面量。</summary>
    public static readonly byte[] Table = BuildTable();

    /// <summary>算一段数据的 CRC-8。</summary>
    public static byte Compute(ReadOnlySpan<byte> data) => Compute(data, 0, data.Length);

    /// <summary>算 <paramref name="data"/> 上 [offset, offset+length) 区间的 CRC-8。</summary>
    public static byte Compute(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > data.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "CRC 区间越界");
        }

        byte crc = 0;
        for (var i = offset; i < offset + length; i++)
        {
            crc = Table[crc ^ data[i]];
        }

        return crc;
    }

    private static byte[] BuildTable()
    {
        const byte polynomial = 0xD5;
        var table = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var crc = (byte)i;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 0x80) != 0
                    ? (byte)((crc << 1) ^ polynomial)
                    : (byte)(crc << 1);
            }

            table[i] = crc;
        }

        return table;
    }
}
