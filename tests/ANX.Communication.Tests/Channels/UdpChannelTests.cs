namespace ANX.Communication.Tests.Channels;

using System.Net;
using System.Net.Sockets;
using ANX.Communication.Channels;
using ANX.Core.Communication;
using ANX.Core.Protocol;
using ANX.Protocol.Gr3;

/// <summary>
/// UDP 通道的收发链路：用本机回环上的一个"假设备"应答 C0 / CA，验证
/// 通道 → 编解码 → 上行解析 这条链能把设备 ID 读回来。
/// </summary>
public sealed class UdpChannelTests
{
    private const string FakeDeviceId = "0123456789ABCDEF01234567";

    [Fact]
    public async Task 联机握手应能读到设备ID()
    {
        Assert.Equal(Gr3Constants.DeviceIdLength, FakeDeviceId.Length);

        using var device = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        device.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var devicePort = ((IPEndPoint)device.LocalEndPoint!).Port;

        using var deviceCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var deviceLoop = Task.Run(() => RunFakeDeviceAsync(device, deviceCts.Token));

        var received = new List<DecodedFrame>();
        var gate = new object();
        var signal = new SemaphoreSlim(0);

        await using var channel = new UdpChannel(
            new UdpChannelOptions(IPAddress.Loopback.ToString(), devicePort)
        );

        channel.DataReceived += (_, e) =>
        {
            var span = e.Data.Span;
            while (Gr3FrameCodec.TryDecodeFrame(span, out var frame, out var consumed))
            {
                lock (gate)
                {
                    received.Add(frame);
                }

                span = span[consumed..];
            }

            signal.Release();
        };

        await channel.OpenAsync();
        Assert.True(channel.IsOpen);

        var connect = Gr3DownlinkCommands.Connect(new DateTime(2026, 10, 10, 9, 30, 0));
        await channel.SendAsync(Gr3FrameCodec.BuildDatagram(connect.CommandCode, connect.Payload, 1));

        var readId = Gr3DownlinkCommands.ReadDeviceId();
        await channel.SendAsync(Gr3FrameCodec.BuildDatagram(readId.CommandCode, readId.Payload, 2));

        var frames = await WaitForFramesAsync(received, gate, expected: 2, timeout: TimeSpan.FromSeconds(10));

        Assert.Equal(Gr3Constants.UplinkAck, frames[0].CommandCode);
        Assert.True(Gr3UplinkParser.TryParseAck(frames[0], out var status));
        Assert.Equal(Gr3AckStatus.Success, status);

        Assert.Equal(Gr3Constants.UplinkDeviceId, frames[1].CommandCode);
        Assert.True(Gr3UplinkParser.TryParseDeviceInfo(frames[1], out var info));
        Assert.Equal(FakeDeviceId, info.Id);
        Assert.Equal(1, info.DeviceType);

        await channel.CloseAsync();
        Assert.False(channel.IsOpen);

        await deviceCts.CancelAsync();
        await deviceLoop;
    }

    [Fact]
    public async Task 未打开时发送应抛异常()
    {
        await using var channel = new UdpChannel(
            new UdpChannelOptions(IPAddress.Loopback.ToString(), 1)
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await channel.SendAsync(new byte[] { 0x01 })
        );
    }

    /// <summary>本机"假设备"：收到 C0 回 A1 成功，收到 CA 回 A4 设备信息。</summary>
    private static async Task RunFakeDeviceAsync(Socket device, CancellationToken token)
    {
        var buffer = new byte[4096];
        var any = new IPEndPoint(IPAddress.Any, 0);

        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await device.ReceiveFromAsync(buffer, SocketFlags.None, any, token);
                var datagram = buffer.AsSpan(0, result.ReceivedBytes);

                if (!Gr3FrameCodec.TryDecodeFrame(datagram, out var frame, out _))
                {
                    continue;
                }

                var reply = frame.CommandCode switch
                {
                    Gr3Constants.DownlinkConnect => Gr3FrameCodec.BuildDatagram(
                        Gr3Constants.UplinkAck,
                        new byte[] { Gr3Constants.AckSuccess },
                        1
                    ),
                    Gr3Constants.DownlinkReadChannelConfig => Gr3FrameCodec.BuildDatagram(
                        Gr3Constants.UplinkDeviceId,
                        Gr3UplinkParser.BuildDeviceInfoPayload(FakeDeviceId, 1),
                        2
                    ),
                    _ => null,
                };

                if (reply is not null)
                {
                    await device.SendToAsync(reply, SocketFlags.None, result.RemoteEndPoint, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 测试结束
        }
        catch (SocketException)
        {
            // socket 关闭
        }
        catch (ObjectDisposedException)
        {
            // socket 关闭
        }
    }

    private static async Task<List<DecodedFrame>> WaitForFramesAsync(
        List<DecodedFrame> frames,
        object gate,
        int expected,
        TimeSpan timeout
    )
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            lock (gate)
            {
                if (frames.Count >= expected)
                {
                    return [.. frames];
                }
            }

            await Task.Delay(20);
        }

        lock (gate)
        {
            Assert.Fail($"等待 {expected} 帧超时，实际收到 {frames.Count} 帧");
            return [.. frames];
        }
    }
}
