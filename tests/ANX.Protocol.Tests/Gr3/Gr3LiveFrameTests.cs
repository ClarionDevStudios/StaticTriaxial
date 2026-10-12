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

    [Fact]
    public void 真机A0应解出版本与通道配置()
    {
        Assert.True(Gr3FrameCodec.TryDecodeFrame(Frames["A0-系统配置"], out var frame, out _));
        Assert.True(Gr3UplinkParser.TryParseSystemInfo(frame, out var info));

        Assert.Equal(10, info.HardwareVersion);
        Assert.Equal(15, info.FirmwareVersion);
        Assert.Equal(10, info.ProtocolVersion);

        Assert.Equal(4, info.PressChannels.Count);
        Assert.Equal(4, info.DisplaceChannels.Count);

        // 压力通道量程：0x000003E8 / 0x00000064 / 0x0000012C / 0
        Assert.Equal(1000, info.PressChannels[0].Scale);
        Assert.Equal(100, info.PressChannels[1].Scale);
        Assert.Equal(300, info.PressChannels[2].Scale);
        Assert.Equal(0, info.PressChannels[3].Scale);

        // 压力通道小数位 2/2/3/0，量纲 1/0/3/0
        Assert.Equal(new byte[] { 2, 2, 3, 0 }, info.PressChannels.Select(c => c.Digital));
        Assert.Equal(new byte[] { 1, 0, 3, 0 }, info.PressChannels.Select(c => c.UnitCode));

        // 位移通道全是 0；微米有效位全 0；围压有效位 1
        Assert.All(info.DisplaceChannels, c => Assert.Equal(0, c.Scale));
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, info.MicrometerValid);
        Assert.Equal(1, info.ConfiningPressureValid);
    }

    [Fact]
    public void 真机A2应解出实时数据各字段()
    {
        // 取数据报里的第一帧（每帧 85 B）
        var first = Frames["A2-实时数据报(5帧)"][..85];

        Assert.True(Gr3FrameCodec.TryDecodeFrame(first, out var frame, out _));
        Assert.True(Gr3UplinkParser.TryParseRealtimeData(frame, out var data));

        // 压力四通道：0 / 100 / -1（0xFFFFFFFF，未接）/ 0
        Assert.Equal(new[] { 0, 100, -1, 0 }, data.PressData);
        Assert.Equal(0, data.ConfiningData);
        Assert.All(data.DisplaceData, v => Assert.Equal(0, v));
        Assert.All(data.MicrometerData, v => Assert.Equal(0, v));

        Assert.Equal(0, data.TargetTemp);
        Assert.Equal(0, data.CurrentTemp);
        Assert.Equal(0.0, data.CurrentTemperatureCelsius);

        // 0x12 = Bit1 位移上限位 + Bit4 电机反转（下降）运行中
        Assert.Equal(
            Gr3MachineStatus.DisplacementUpperLimit | Gr3MachineStatus.MotorReverse,
            data.MachineStatus
        );
        Assert.Equal(0x12, (byte)data.MachineStatus);

        // 0x00 = 停止 / 未暂停 / 未找到破坏点 / 非保持
        Assert.Equal(Gr3TestStatus.None, data.TestStatus);
        Assert.Equal(0x00, (byte)data.TestStatus);
        Assert.Equal(0x0000, data.PortOutputBStatus);
        Assert.Equal(0x00, data.PortInputAStatus);
        Assert.Equal(0x0102, data.PortInputBStatus);
        Assert.Equal(new[] { 0, 0 }, data.ExtendedExtensometer);
    }

    [Fact]
    public void 真机A3通道一应解出量程小数位量纲与标定点()
    {
        Assert.True(Gr3FrameCodec.TryDecodeFrame(Frames["A3-通道配置(通道1)"], out var frame, out _));
        Assert.True(Gr3UplinkParser.TryParseChannelConfig(frame, out var config));

        Assert.Equal(1, config.ChannelId);
        Assert.Equal(1000, config.Config.Scale);
        Assert.Equal(2, config.Config.Digital);
        Assert.Equal(1, config.Config.UnitCode); // 1 = 载荷 kN

        Assert.Equal(Gr3Constants.CalibrationPointCount, config.CalibrationRealValues.Count);
        Assert.Equal(Gr3Constants.CalibrationPointCount, config.CalibrationCodeValues.Count);

        // 标定实值 9900 → 100000（配合 Digital=2 即 99.00 → 1000.00 kN，正好等于 Range=1000）
        Assert.Equal(new[] { 9900, 12300, 20000, 30000, 40000, 50000, 60000, 70000, 80000, 90000, 100000 }, config.CalibrationRealValues);
        Assert.Equal(new[] { 100, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000 }, config.CalibrationCodeValues);

        // 由标定表反推的换算口径：raw / 10^Digital
        Assert.Equal(99.0, config.Config.ToEngineeringValue(config.CalibrationRealValues[0]));
        Assert.Equal(1000.0, config.Config.ToEngineeringValue(config.CalibrationRealValues[^1]));
    }

    [Fact]
    public void 真机A3越界通道应回零配置且不报错()
    {
        // 协议写通道号取值 1-12；请求 0（越界）时设备不报错，回一帧通道配置帧，
        // 其中通道号 / 量程 / 小数位 / 量纲都是 0，标定段是未定义内容（故不对其断言）。
        Assert.True(Gr3FrameCodec.TryDecodeFrame(Frames["A3-通道配置(通道0越界)"], out var frame, out _));
        Assert.True(Gr3UplinkParser.TryParseChannelConfig(frame, out var config));

        Assert.Equal(0, config.ChannelId);
        Assert.Equal(0, config.Config.Scale);
        Assert.Equal(0, config.Config.Digital);
        Assert.Equal(0, config.Config.UnitCode);
        Assert.Equal(Gr3Constants.CalibrationPointCount, config.CalibrationRealValues.Count);
        Assert.Equal(Gr3Constants.CalibrationPointCount, config.CalibrationCodeValues.Count);
    }

    [Fact]
    public void 长度不符的上行数据域应被判失败()
    {
        // 把 A0 的数据域截短一个字节：解析器必须拒绝，而不是按错位读出垃圾值
        Assert.True(Gr3FrameCodec.TryDecodeFrame(Frames["A0-系统配置"], out var a0, out _));
        var truncated = new ANX.Core.Protocol.DecodedFrame(
            a0.CommandCode,
            a0.Status,
            a0.Payload[..^1]
        );

        Assert.False(Gr3UplinkParser.TryParseSystemInfo(truncated, out _));

        // 命令字对不上也应拒绝
        Assert.False(Gr3UplinkParser.TryParseRealtimeData(a0, out _));
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
