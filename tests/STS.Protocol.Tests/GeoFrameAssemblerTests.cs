namespace STS.Protocol.Tests;

using STS.Protocol.Geo;

/// <summary>字节流装配：分片、噪声重同步、一包多帧、坏帧丢弃。</summary>
public sealed class GeoFrameAssemblerTests
{
    [Fact]
    public void 分片喂入应还原整帧()
    {
        var connect = GeoCommands.Connect(order: 1);
        var assembler = new GeoFrameAssembler();

        Assert.Empty(assembler.Feed(connect.AsSpan(0, 3)));
        Assert.Empty(assembler.Feed(connect.AsSpan(3, 2)));

        var frames = assembler.Feed(connect.AsSpan(5));

        var frame = Assert.Single(frames);
        Assert.Equal(0xC0, frame.Command);
        Assert.Equal(0L, assembler.ResyncByteCount);
    }

    [Fact]
    public void 前缀噪声应被跳过后重同步()
    {
        var motor = GeoCommands.Motor(GeoCommands.MotorOrder.Up, 2400);
        var chunk = new byte[] { 0x13, 0x37 }.Concat(motor).ToArray();
        var assembler = new GeoFrameAssembler();

        var frames = assembler.Feed(chunk);

        var frame = Assert.Single(frames);
        Assert.Equal(0xC1, frame.Command);
        Assert.Equal(2L, assembler.ResyncByteCount);
    }

    [Fact]
    public void 一包多帧应按序解出()
    {
        var connect = GeoCommands.Connect(order: 1);
        var state = GeoCommands.TestState(action: 1);
        var chunk = connect.Concat(state).ToArray();
        var assembler = new GeoFrameAssembler();

        var frames = assembler.Feed(chunk);

        Assert.Equal(2, frames.Count);
        Assert.Equal(0xC0, frames[0].Command);
        Assert.Equal(0xC3, frames[1].Command);
        Assert.Equal(0L, assembler.ResyncByteCount);
    }

    [Fact]
    public void 坏帧应丢弃且后续帧仍能解出()
    {
        var broken = GeoCommands.Connect(order: 1);
        broken[^1] ^= 0xFF;
        var good = GeoCommands.TestState(action: 1);
        var assembler = new GeoFrameAssembler();

        var frames = assembler.Feed(broken.Concat(good).ToArray());

        var frame = Assert.Single(frames);
        Assert.Equal(0xC3, frame.Command);
        Assert.Equal(8L, assembler.ResyncByteCount); // 坏帧 8 字节被逐字节跳过（含首个 FA）
    }
}
