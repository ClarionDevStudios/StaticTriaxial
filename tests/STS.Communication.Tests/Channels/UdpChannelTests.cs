namespace STS.Communication.Tests.Channels;

using System.Net;
using System.Net.Sockets;
using STS.Communication;
using STS.Communication.Channels;

/// <summary>UDP 通道：裸字节收发（回环），不做任何帧解析 —— 帧语义由协议层负责。</summary>
public sealed class UdpChannelTests
{
    [Fact]
    public async Task 回环收发应原样收到字节()
    {
        using var device = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        device.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var deviceEndPoint = (IPEndPoint)device.LocalEndPoint!;

        await using var channel = new UdpChannel(
            new UdpChannelOptions(IPAddress.Loopback.ToString(), deviceEndPoint.Port, 0)
        );

        var received = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        channel.DataReceived += (_, chunk) => received.TrySetResult(chunk.Data.ToArray());

        await channel.OpenAsync();
        Assert.True(channel.IsOpen);

        var payload = new byte[] { 0xFA, 0xFB, 0xFC, 0x01, 0x5A };
        await channel.SendAsync(payload);

        // 假设备收到后原样回发
        var buffer = new byte[64];
        var from = new IPEndPoint(IPAddress.Any, 0);
        var read = await device.ReceiveFromAsync(buffer, SocketFlags.None, from);
        Assert.Equal(payload, buffer.Take(read.ReceivedBytes).ToArray());
        _ = device.SendTo(payload, read.RemoteEndPoint!);

        var actual = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(payload, actual);

        await channel.CloseAsync();
        Assert.False(channel.IsOpen);
    }

    [Fact]
    public void 远端地址无效应抛错()
    {
        var channel = new UdpChannel(new UdpChannelOptions("not-an-ip", 5001));

        Assert.Throws<ArgumentException>(() =>
        {
            _ = channel.OpenAsync();
        });
    }
}
