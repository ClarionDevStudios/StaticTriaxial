namespace STS.Protocol.Tests;

using STS.Protocol.Geo;

/// <summary>回环通道：把真实协议栈接到模拟下位机上（同步应答 + 按节拍 A1 流）。</summary>
public sealed class GeoLoopbackChannelTests
{
    [Fact]
    public async Task 发送联机应同步收到A0应答()
    {
        await using var channel = new GeoLoopbackChannel(new GeoSimulator());
        var received = new List<byte>();
        channel.DataReceived += (_, chunk) => received.AddRange(chunk.Data.ToArray());

        await channel.OpenAsync();
        await channel.SendAsync(GeoCommands.Connect(order: 1));

        var frame = Assert.Single(new GeoFrameAssembler().Feed(received.ToArray()));
        Assert.True(GeoSystemConfig.TryParse(frame, out _));

        await channel.CloseAsync();
    }

    [Fact]
    public async Task 联机后应按节拍收到A1流且关闭后停止()
    {
        await using var channel = new GeoLoopbackChannel(
            new GeoSimulator(),
            tickInterval: TimeSpan.FromMilliseconds(10)
        );
        var assembler = new GeoFrameAssembler();
        var a1Count = 0;
        channel.DataReceived += (_, chunk) =>
        {
            foreach (var frame in assembler.Feed(chunk.Data.ToArray()))
            {
                if (frame.Command == GeoCommands.RealtimeCommand)
                {
                    Interlocked.Increment(ref a1Count);
                }
            }
        };

        await channel.OpenAsync();
        await channel.SendAsync(GeoCommands.Connect(order: 1));
        await channel.SendAsync(GeoCommands.Connect(order: 2));

        var deadline = DateTime.UtcNow.AddSeconds(2);

        while (Volatile.Read(ref a1Count) < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(
            Volatile.Read(ref a1Count) >= 3,
            $"应至少收到 3 帧 A1，实际 {a1Count}"
        );

        await channel.CloseAsync();
        var countAtClose = Volatile.Read(ref a1Count);
        await Task.Delay(80);

        Assert.Equal(countAtClose, Volatile.Read(ref a1Count));
    }
}
