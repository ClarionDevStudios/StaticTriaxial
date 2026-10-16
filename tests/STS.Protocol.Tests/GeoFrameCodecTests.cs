namespace STS.Protocol.Tests;

using STS.Protocol.Geo;

/// <summary>帧构建字节布局（与现役逐字节构建一致）+ 流式解帧的三种状态。</summary>
public sealed class GeoFrameCodecTests
{
    [Fact]
    public void 构建C0联机应与现役逐字节一致()
    {
        Assert.Equal(
            new byte[] { 0xFA, 0xFB, 0xFC, 0x04, 0xC0, 0x4F, 0x01, 0x6E },
            GeoCommands.Connect(order: 1)
        );
    }

    [Fact]
    public void 构建C1电机应与现役逐字节一致()
    {
        Assert.Equal(
            new byte[] { 0xFA, 0xFB, 0xFC, 0x05, 0xC1, 0x01, 0x09, 0x60, 0x21 },
            GeoCommands.Motor(GeoCommands.MotorOrder.Up, 2400)
        );
    }

    [Fact]
    public void 构建C3试验状态应与现役逐字节一致()
    {
        Assert.Equal(
            new byte[] { 0xFA, 0xFB, 0xFC, 0x04, 0xC3, 0x54, 0x01, 0xCB },
            GeoCommands.TestState(action: 1)
        );
    }

    [Fact]
    public void 构建C2围压应与现役逐字节一致()
    {
        var frame = GeoCommands.ConfiningPressure(
            confiningTargetKPa: 30,
            backTargetKPa: 0,
            startConfining: true,
            startBack: false,
            confiningSpeedRaw: 400,
            backSpeedRaw: 0,
            stableSpeedRaw: 50,
            loadingRodAreaRaw: 19635
        );

        Assert.Equal(
            new byte[]
            {
                0xFA, 0xFB, 0xFC, 0x16, 0xC2, // 头 + 长度 22（= 1 + 20 数据 + 1 CRC）
                0x00, 0x00, 0x00, 0x1E, // 围压目标 30kPa
                0x00, 0x00, 0x00, 0x00, // 反压目标 0
                0x01, 0x00, // 围压启动 / 反压停止
                0x01, 0x90, // 围压速度 400（0.1mm/min）
                0x00, 0x00, // 反压速度 0
                0x00, 0x32, // 稳压速度 50（0.1mm/min）
                0x00, 0x00, 0x4C, 0xB3, // 加载杆面积 19635
                0xB6, // CRC（现役算法）
            },
            frame
        );
    }

    [Fact]
    public void 解帧应能往返()
    {
        var built = GeoCommands.Motor(GeoCommands.MotorOrder.Down, 0x1388);

        var status = GeoFrameCodec.TryDecode(built, out var frame, out var consumed);

        Assert.Equal(GeoDecodeStatus.Ok, status);
        Assert.Equal(9, consumed);
        Assert.NotNull(frame);
        Assert.Equal(0xC1, frame.Command);
        Assert.Equal(new byte[] { 0x02, 0x13, 0x88 }, frame.Data);
    }

    [Fact]
    public void 数据不足应等更多字节()
    {
        var status = GeoFrameCodec.TryDecode(
            [0xFA, 0xFB, 0xFC, 0x04, 0xC0],
            out var frame,
            out var consumed
        );

        Assert.Equal(GeoDecodeStatus.NeedMoreData, status);
        Assert.Equal(0, consumed);
        Assert.Null(frame);
    }

    [Fact]
    public void 坏CRC应报无效并消费一字节()
    {
        var built = GeoCommands.Connect(order: 1);
        built[^1] ^= 0xFF;

        var status = GeoFrameCodec.TryDecode(built, out _, out var consumed);

        Assert.Equal(GeoDecodeStatus.Invalid, status);
        Assert.Equal(1, consumed);
    }

    [Fact]
    public void 非帧头与非法长度都应报无效()
    {
        Assert.Equal(
            GeoDecodeStatus.Invalid,
            GeoFrameCodec.TryDecode([0x00, 0x01, 0x02, 0x03], out _, out var consumedA)
        );
        Assert.Equal(1, consumedA);

        // 长度字节 = 1（不足"指令符 + CRC"）
        Assert.Equal(
            GeoDecodeStatus.Invalid,
            GeoFrameCodec.TryDecode([0xFA, 0xFB, 0xFC, 0x01, 0x00, 0x00], out _, out var consumedB)
        );
        Assert.Equal(1, consumedB);
    }
}
