namespace ANX.Protocol.Tests.Gr3;

using ANX.Core.Protocol;
using ANX.Protocol.Gr3;

/// <summary>
/// 帧布局与拆包。结构断言直接来自协议文档第一章；CRC 的位置与覆盖范围单独断言
/// （CRC 表本身已由 <see cref="Gr3Crc8Tests"/> 与现役字面量逐项核对过）。
/// </summary>
public sealed class Gr3FrameCodecTests
{
    private static readonly DateTime Sample = new(2026, 10, 10, 9, 30, 0);

    [Fact]
    public void 联机帧的长度与结构应逐字节符合协议()
    {
        var payload = Gr3DownlinkCommands.Connect(Sample).Payload;
        var frame = Gr3FrameCodec.BuildDatagram(Gr3Constants.DownlinkConnect, payload, 1);

        // 外层：帧头(3) + 长度(1) + 包编号(2) + 指令包(4+12) + 校验和(2) = 24
        Assert.Equal(24, frame.Length);
        Assert.Equal(new byte[] { 0xEE, 0xEE, 0xEE }, frame[..3]);
        Assert.Equal(20, frame[3]); // 数据包长度 = 包编号 + 指令包 + 校验和
        Assert.Equal(new byte[] { 0x00, 0x01 }, frame[4..6]);

        // 内层：同步符(3) + 帧长度(1) + 命令类型(1) + 数据(10) + CRC(1)
        Assert.Equal(new byte[] { 0xFA, 0xFB, 0xFC }, frame[6..9]);
        Assert.Equal(12, frame[9]);
        Assert.Equal(Gr3Constants.DownlinkConnect, frame[10]);
        Assert.Equal(payload, frame[11..21]);

        // CRC 位置与覆盖范围：从"帧长度"字节起 12 个字节
        Assert.Equal(Gr3Crc8.Compute(frame, 9, 12), frame[21]);

        // UDP 校验和：除自身外全部字节累加取低 16 位（大端）
        var sum = 0;
        for (var i = 0; i < 22; i++)
        {
            sum += frame[i];
        }

        Assert.Equal((byte)((sum >> 8) & 0xFF), frame[22]);
        Assert.Equal((byte)(sum & 0xFF), frame[23]);
    }

    [Fact]
    public void A1应答帧应与协议文档的示例布局一致()
    {
        // 文档 2.2 节举例：EE EE EE  0B  00  01  FA FB FC  03  A1  ORDER  CRC  SUM(2)
        var frame = Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkAck, new byte[] { 0x01 }, 1);

        Assert.Equal(15, frame.Length);
        Assert.Equal(
            new byte[] { 0xEE, 0xEE, 0xEE, 0x0B, 0x00, 0x01, 0xFA, 0xFB, 0xFC, 0x03, 0xA1 },
            frame[..11]
        );
    }

    [Fact]
    public void 编解码应往返一致()
    {
        var payload = Gr3DownlinkCommands.MotorLoad(0x55, 500).Payload;
        var frame = Gr3FrameCodec.BuildDatagram(Gr3Constants.DownlinkMotorLoad, payload, 42);

        Assert.True(Gr3FrameCodec.TryDecodeFrame(frame, out var decoded, out var consumed));
        Assert.Equal(frame.Length, consumed);
        Assert.Equal(Gr3Constants.DownlinkMotorLoad, decoded.CommandCode);
        Assert.Equal(0, decoded.Status);
        Assert.Equal(payload, decoded.Payload.ToArray());
    }

    [Fact]
    public void A1应答的状态应取到Status其余数据进Payload()
    {
        var frame = Gr3FrameCodec.BuildDatagram(
            Gr3Constants.UplinkAck,
            new byte[] { Gr3Constants.AckSuccess, 0xAA, 0xBB },
            7
        );

        Assert.True(Gr3FrameCodec.TryDecodeFrame(frame, out var decoded, out _));
        Assert.Equal(Gr3Constants.AckSuccess, decoded.Status);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, decoded.Payload.ToArray());
        Assert.True(Gr3UplinkParser.TryParseAck(decoded, out var status));
        Assert.Equal(Gr3AckStatus.Success, status);
    }

    [Fact]
    public void 单数据报多帧应逐帧解出()
    {
        var first = Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkAck, new byte[] { 1 }, 1);
        var second = Gr3FrameCodec.BuildDatagram(
            Gr3Constants.UplinkDeviceId,
            Gr3UplinkParser.BuildDeviceInfoPayload("0123456789ABCDEF01234567", 1),
            2
        );

        var datagram = new byte[first.Length + second.Length];
        first.CopyTo(datagram, 0);
        second.CopyTo(datagram, first.Length);

        var decoded = new List<DecodedFrame>();
        var span = datagram.AsSpan();
        while (Gr3FrameCodec.TryDecodeFrame(span, out var frame, out var consumed))
        {
            decoded.Add(frame);
            span = span[consumed..];
        }

        Assert.Equal(2, decoded.Count);
        Assert.Equal(Gr3Constants.UplinkAck, decoded[0].CommandCode);
        Assert.Equal(Gr3Constants.UplinkDeviceId, decoded[1].CommandCode);
        Assert.True(Gr3UplinkParser.TryParseDeviceInfo(decoded[1], out var info));
        Assert.Equal("0123456789ABCDEF01234567", info.Id);
        Assert.Equal(1, info.DeviceType);
        Assert.Equal("公路版万能机", info.DeviceTypeName);
    }

    [Fact]
    public void UDP校验和被篡改时应拒绝并只前进一字节()
    {
        var frame = Gr3FrameCodec.BuildDatagram(Gr3Constants.DownlinkConnect, Gr3DownlinkCommands.Connect(Sample).Payload, 1);
        frame[12] ^= 0xFF; // 改数据字节，不动校验和

        Assert.False(Gr3FrameCodec.TryDecodeFrame(frame, out _, out var consumed));
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void 指令包CRC错误时应丢弃整帧()
    {
        var frame = Gr3FrameCodec.BuildDatagram(Gr3Constants.DownlinkConnect, Gr3DownlinkCommands.Connect(Sample).Payload, 1);
        frame[12] ^= 0xFF; // 改数据字节
        FixUdpChecksum(frame); // 只把外层校验和修好，内层 CRC 仍然是旧的

        Assert.False(Gr3FrameCodec.TryDecodeFrame(frame, out _, out var consumed));
        Assert.Equal(frame.Length, consumed);
    }

    [Fact]
    public void 不完整帧应返回零消费等待更多数据()
    {
        var frame = Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkAck, new byte[] { 1 }, 1);

        Assert.False(Gr3FrameCodec.TryDecodeFrame(frame.AsSpan(0, frame.Length - 1), out _, out var consumed));
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void 前导噪声应被跳过()
    {
        var frame = Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkAck, new byte[] { 1 }, 1);
        var buffer = new byte[] { 0x00, 0x11, 0x22 }.Concat(frame).ToArray();

        Assert.False(Gr3FrameCodec.TryDecodeFrame(buffer, out _, out var consumed));
        Assert.Equal(3, consumed);

        Assert.True(Gr3FrameCodec.TryDecodeFrame(buffer.AsSpan(consumed), out var decoded, out _));
        Assert.Equal(Gr3Constants.UplinkAck, decoded.CommandCode);
    }

    [Fact]
    public void 指令包超过协议上限应抛异常()
    {
        var tooLong = new byte[0x9E];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Gr3FrameCodec.BuildDatagram(Gr3Constants.DownlinkConnect, tooLong, 1)
        );
    }

    [Fact]
    public void 包编号应从一起递增且可复位()
    {
        var codec = new Gr3FrameCodec();

        Assert.Equal(1, codec.NextSequence());
        Assert.Equal(2, codec.NextSequence());

        codec.ResetSequence();
        Assert.Equal(1, codec.NextSequence());

        // 编码走的是同一个计数器
        codec.ResetSequence();
        var frame = codec.Encode(Gr3DownlinkCommands.Heartbeat());
        Assert.Equal(new byte[] { 0x00, 0x01 }, frame[4..6]);
    }

    /// <summary>把外层校验和按当前内容重算（只用于构造"外层对、内层坏"的用例）。</summary>
    private static void FixUdpChecksum(byte[] frame)
    {
        var sum = 0;
        for (var i = 0; i < frame.Length - 2; i++)
        {
            sum += frame[i];
        }

        frame[^2] = (byte)((sum >> 8) & 0xFF);
        frame[^1] = (byte)(sum & 0xFF);
    }
}
