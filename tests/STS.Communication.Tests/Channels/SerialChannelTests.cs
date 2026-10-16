namespace STS.Communication.Tests.Channels;

using STS.Communication;
using STS.Communication.Channels;
using STS.Core.Communication;

/// <summary>串口通道：开 / 关 / 收发 / 出错路径（假串口设备注入，不需要真硬件）。</summary>
public sealed class SerialChannelTests
{
    private sealed class FakeSerialPort : ISerialPortDevice
    {
        private readonly Queue<byte> _pending = new();

        public bool IsOpen { get; private set; }

        public bool ThrowOnOpen { get; set; }

        public int BytesToRead => _pending.Count;

        public List<byte[]> Writes { get; } = [];

        public event Action? DataReceived;

        public event Action<string>? ErrorRaised;

        public void Open()
        {
            if (ThrowOnOpen)
            {
                throw new IOException("端口被占用");
            }

            IsOpen = true;
        }

        public void Close() => IsOpen = false;

        public int Read(byte[] buffer, int offset, int count)
        {
            var n = Math.Min(count, _pending.Count);

            for (var i = 0; i < n; i++)
            {
                buffer[offset + i] = _pending.Dequeue();
            }

            return n;
        }

        public void Write(byte[] buffer, int offset, int count) =>
            Writes.Add(buffer[offset..(offset + count)]);

        public void Enqueue(params byte[] data)
        {
            foreach (var b in data)
            {
                _pending.Enqueue(b);
            }
        }

        public void RaiseData() => DataReceived?.Invoke();

        public void RaiseError(string message) => ErrorRaised?.Invoke(message);

        public void Dispose() => IsOpen = false;
    }

    [Fact]
    public async Task 打开后应报告已打开_显示名含端口与波特率()
    {
        var port = new FakeSerialPort();
        await using var channel = new SerialChannel(
            new SerialChannelOptions("COM9", 115200),
            () => port
        );

        Assert.Equal(ChannelKind.Serial, channel.Kind);
        Assert.Equal("串口 COM9@115200", channel.DisplayName);
        Assert.False(channel.IsOpen);

        await channel.OpenAsync();

        Assert.True(channel.IsOpen);
        Assert.True(port.IsOpen);
    }

    [Fact]
    public async Task 发送字节应原样写到串口()
    {
        var port = new FakeSerialPort();
        await using var channel = new SerialChannel(new SerialChannelOptions("COM9"), () => port);

        await channel.OpenAsync();
        await channel.SendAsync(new byte[] { 0xFA, 0xFB, 0xFC, 0x01, 0x5A });

        var written = Assert.Single(port.Writes);
        Assert.Equal(new byte[] { 0xFA, 0xFB, 0xFC, 0x01, 0x5A }, written);
    }

    [Fact]
    public async Task 收到字节应触发回调且内容一致()
    {
        var port = new FakeSerialPort();
        await using var channel = new SerialChannel(new SerialChannelOptions("COM9"), () => port);

        var received = new List<byte[]>();
        channel.DataReceived += (_, chunk) => received.Add(chunk.Data.ToArray());

        await channel.OpenAsync();
        port.Enqueue(0x01, 0x02, 0x03);
        port.RaiseData();

        var chunk = Assert.Single(received);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, chunk);
    }

    [Fact]
    public async Task 关闭后不再接受发送且可重复关闭()
    {
        var port = new FakeSerialPort();
        await using var channel = new SerialChannel(new SerialChannelOptions("COM9"), () => port);

        await channel.OpenAsync();
        await channel.CloseAsync();
        await channel.CloseAsync();

        Assert.False(channel.IsOpen);
        Assert.False(port.IsOpen);

        await channel.SendAsync(new byte[] { 0x01 });

        Assert.Empty(port.Writes);
    }

    [Fact]
    public void 打开失败应抛出且不保持打开()
    {
        var port = new FakeSerialPort { ThrowOnOpen = true };
        var channel = new SerialChannel(new SerialChannelOptions("COM9"), () => port);

        Assert.Throws<IOException>(() =>
        {
            _ = channel.OpenAsync();
        });

        Assert.False(channel.IsOpen);
    }

    [Fact]
    public async Task 串口错误事件应把通道置为关闭()
    {
        var port = new FakeSerialPort();
        await using var channel = new SerialChannel(new SerialChannelOptions("COM9"), () => port);

        await channel.OpenAsync();
        port.RaiseError("frame error");

        Assert.False(channel.IsOpen);
    }
}
