namespace ANX.Protocol.Tests.Gr3;

using ANX.Protocol.Gr3;

/// <summary>
/// 真机抓帧黄金用例。帧于 2026-10-10 从实体 GR-3 控制器（192.168.0.122:5001）抓回，
/// 探针只发了 C0 联机 / CB 心跳 / CAH 读设备 ID / CAH 读通道配置。
/// 夹具 `Gr3/Frames/gr3-live-20261010.hex.txt` 是从抓帧日志里**机器提取**的，不是手抄。
///
/// 这些用例的价值：只要 ANX 的帧长度、CRC-8、UDP 校验和、命令字任何一处与真机不符，
/// 解码就会失败 —— 它们同时锁住了 M2 的全部协议假设。
/// </summary>
public sealed class Gr3LiveFrameTests
{
    private static readonly Dictionary<string, byte[]> Frames = LoadFixture();

    [Fact]
    public void 真机A0系统配置帧应能解出()
    {
        var raw = Frames["A0-系统配置"];

        Assert.Equal(70, raw.Length);
        Assert.True(Gr3FrameCodec.TryDecodeFrame(raw, out var frame, out var consumed));
        Assert.Equal(raw.Length, consumed);
        Assert.Equal(Gr3Constants.UplinkSystemInfo, frame.CommandCode);
        Assert.Equal(56, frame.Payload.Length);
    }

    [Fact]
    public void 真机A1应答帧应能解出且状态为成功()
    {
        var raw = Frames["A1-应答成功"];

        Assert.Equal(15, raw.Length);
        Assert.Equal(new byte[] { 0xEE, 0xEE, 0xEE, 0x0B }, raw[..4]); // 与文档 2.2 节举例的头部一致

        Assert.True(Gr3FrameCodec.TryDecodeFrame(raw, out var frame, out _));
        Assert.Equal(Gr3Constants.UplinkAck, frame.CommandCode);
        Assert.True(Gr3UplinkParser.TryParseAck(frame, out var status));
        Assert.Equal(Gr3AckStatus.Success, status);
    }

    [Fact]
    public void 真机A4设备信息帧应能解出ID与类型()
    {
        var raw = Frames["A4-设备ID"];

        Assert.Equal(40, raw.Length);
        Assert.True(Gr3FrameCodec.TryDecodeFrame(raw, out var frame, out _));
        Assert.Equal(Gr3Constants.UplinkDeviceId, frame.CommandCode);
        Assert.Equal(Gr3Constants.DeviceInfoLength, frame.Payload.Length); // 24 + 2

        Assert.True(Gr3UplinkParser.TryParseDeviceInfo(frame, out var info));
        Assert.Equal("1400C2009105032453235302", info.Id);
        Assert.Equal(24, info.Id.Length);
        Assert.Equal(2, info.DeviceType); // 2 = 伺服版综合
        Assert.Equal("伺服版综合", info.DeviceTypeName);
    }

    [Fact]
    public void 真机A3通道配置帧应能解出()
    {
        var raw = Frames["A3-通道配置"];

        Assert.Equal(109, raw.Length);
        Assert.True(Gr3FrameCodec.TryDecodeFrame(raw, out var frame, out _));
        Assert.Equal(Gr3Constants.UplinkChannelConfig, frame.CommandCode);
        Assert.Equal(95, frame.Payload.Length);
    }

    [Fact]
    public void 真机单个数据报应打包五帧A2且全部能解出()
    {
        var raw = Frames["A2-实时数据报(5帧)"];

        Assert.Equal(425, raw.Length); // 5 × 85

        var decoded = new List<ANX.Core.Protocol.DecodedFrame>();
        var span = raw.AsSpan();
        while (Gr3FrameCodec.TryDecodeFrame(span, out var frame, out var consumed))
        {
            decoded.Add(frame);
            span = span[consumed..];
        }

        Assert.Equal(5, decoded.Count);
        Assert.All(decoded, f => Assert.Equal(Gr3Constants.UplinkRealtimeData, f.CommandCode));
        Assert.All(decoded, f => Assert.Equal(71, f.Payload.Length));
        Assert.Empty(span.ToArray()); // 425 字节被正好消费完，没有残留
    }

    [Fact]
    public void 真机帧的包编号应逐帧递增()
    {
        var raw = Frames["A2-实时数据报(5帧)"];

        var sequences = new List<ushort>();
        var span = raw.AsSpan();
        while (Gr3FrameCodec.TryDecodeFrame(span, out _, out var consumed))
        {
            // 包编号固定在外层偏移 4..5（大端）
            sequences.Add((ushort)((span[4] << 8) | span[5]));
            span = span[consumed..];
        }

        Assert.Equal(5, sequences.Count);
        for (var i = 1; i < sequences.Count; i++)
        {
            Assert.Equal(sequences[i - 1] + 1, sequences[i]);
        }
    }

    private static Dictionary<string, byte[]> LoadFixture()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Gr3",
            "Frames",
            "gr3-live-20261010.hex.txt"
        );

        Assert.True(File.Exists(path), $"抓帧夹具不存在：{path}");

        var frames = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('|');
            Assert.True(separator > 0, $"夹具行格式应为「名称|十六进制」：{line}");

            var name = line[..separator].Trim();
            var hex = line[(separator + 1)..].Trim();
            frames[name] = Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
        }

        return frames;
    }
}
