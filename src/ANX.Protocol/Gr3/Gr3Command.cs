namespace ANX.Protocol.Gr3;

using System.Buffers.Binary;
using System.Text;
using ANX.Core.Protocol;

/// <summary>一条 GR-3 指令：命令字 + 数据域（不含帧头、长度与校验）。</summary>
public sealed class Gr3Command(byte commandCode, byte[] payload) : ICommand
{
    public byte CommandCode { get; } = commandCode;

    public byte[] Payload { get; } = payload;

    public byte[] BuildPayload() => Payload;
}

/// <summary>
/// 下行指令数据域的构造器。多字节字段一律按**大端**写入
/// （协议原文：2 字节数遵循高字节在前的传输原则）。
/// </summary>
public sealed class Gr3CommandWriter
{
    private readonly List<byte> _data = [];

    public Gr3CommandWriter WriteByte(byte value)
    {
        _data.Add(value);
        return this;
    }

    public Gr3CommandWriter WriteInt16(short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        _data.AddRange(buffer.ToArray());
        return this;
    }

    public Gr3CommandWriter WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        _data.AddRange(buffer.ToArray());
        return this;
    }

    public Gr3CommandWriter WriteInt32(int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        _data.AddRange(buffer.ToArray());
        return this;
    }

    /// <summary>写固定长度的 ASCII 串，不足补空格（C2H 试验编号用）。</summary>
    public Gr3CommandWriter WriteAscii(string value, int fixedLength)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        for (var i = 0; i < fixedLength; i++)
        {
            _data.Add(i < bytes.Length ? bytes[i] : (byte)' ');
        }

        return this;
    }

    public byte[] ToArray() => [.. _data];
}
