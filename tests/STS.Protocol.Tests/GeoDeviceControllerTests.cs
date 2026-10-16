namespace STS.Protocol.Tests;

using System.Buffers.Binary;
using STS.Core;
using STS.Core.Communication;
using STS.Core.Control;
using STS.Protocol.Geo;

/// <summary>
/// 设备控制器（真实协议栈 → 模拟下位机）：握手、启动序（C3 先于 C1）、运行中改速、
/// 停止序、换向先停后转、软件调零、超时兜底。用同步假通道驱动，全程确定性。
/// </summary>
public sealed class GeoDeviceControllerTests
{
    private sealed class RecordingChannel(GeoSimulator simulator) : ICommChannel
    {
        public List<byte[]> Sent { get; } = [];

        public bool IsOpen { get; private set; }

        public ChannelKind Kind => ChannelKind.Simulator;

        public string DisplayName => "回环（测试）";

        public event EventHandler<ReceivedChunk>? DataReceived;

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default
        )
        {
            Sent.Add(data.ToArray());
            Raise(simulator.Feed(data.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }

        /// <summary>人为推进一拍摄样（返回的 A1 原样从 DataReceived 送出）。</summary>
        public void Pump(double seconds) => Raise(simulator.Tick(seconds));

        private void Raise(byte[] bytes)
        {
            if (bytes.Length > 0)
            {
                DataReceived?.Invoke(this, new ReceivedChunk(bytes));
            }
        }
    }

    private sealed class SilentChannel : ICommChannel
    {
        public bool IsOpen { get; private set; }

        public ChannelKind Kind => ChannelKind.Simulator;

        public string DisplayName => "不回话（测试）";

        public event EventHandler<ReceivedChunk>? DataReceived;

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task CloseAsync()
        {
            IsOpen = false;
            return Task.CompletedTask;
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task 联机应完成握手并收到采样()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            TestSample? latest = null;
            controller.SampleReceived += (_, sample) => latest = sample;

            await controller.ConnectAsync();

            Assert.True(controller.IsConnected);
            Assert.Equal(25.0, controller.DisplacementRangeMillimeters!.Value, 3);
            Assert.Equal(30.0, controller.LoadRangeKN!.Value, 3);

            var frames = Decode(channel.Sent);
            Assert.Equal(2, frames.Count);
            Assert.Equal(0xC0, frames[0].Command);
            Assert.Equal(1, frames[0].Data[1]);
            Assert.Equal(0xC0, frames[1].Command);
            Assert.Equal(2, frames[1].Data[1]);

            channel.Pump(1.0);

            Assert.NotNull(latest);
            Assert.Equal(0.0, latest.Value.Load, 3);
            Assert.Equal(10.0, latest.Value.RawDisplacement, 3); // 未调零：净值 = 原始
            Assert.Equal(10.0, latest.Value.Displacement, 3);
        }
    }

    [Fact]
    public async Task 启动序应为C3先于C1并换算速率()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            await controller.SetSpeedAsync(4.59);
            channel.Sent.Clear();

            await controller.StartAsync();

            var frames = Decode(channel.Sent);
            Assert.Equal(2, frames.Count);
            Assert.Equal(0xC3, frames[0].Command); // 先 C3('T',1)
            Assert.Equal(0x54, frames[0].Data[0]);
            Assert.Equal(1, frames[0].Data[1]);
            Assert.Equal(0xC1, frames[1].Command); // 再 C1（上升，4.59mm/min → 4590）
            Assert.Equal(1, frames[1].Data[0]);
            Assert.Equal(4590, (frames[1].Data[1] << 8) | frames[1].Data[2]);

            TestSample? latest = null;
            controller.SampleReceived += (_, sample) => latest = sample;
            channel.Pump(60.0);

            Assert.NotNull(latest);
            Assert.Equal(0.734, latest.Value.Load, 1e-3); // 0.16kN/mm × 4.59mm
            Assert.Equal(14.59, latest.Value.RawDisplacement, 0.01);
            Assert.True(controller.IsMotorRunning);
        }
    }

    [Fact]
    public async Task 运行中改速应重发C1且不换向()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            await controller.SetSpeedAsync(4.59);
            await controller.StartAsync();
            channel.Sent.Clear();

            await controller.SetSpeedAsync(2.0);

            var frame = Assert.Single(Decode(channel.Sent));
            Assert.Equal(0xC1, frame.Command);
            Assert.Equal(1, frame.Data[0]); // 仍为"上升"
            Assert.Equal(2000, (frame.Data[1] << 8) | frame.Data[2]);
        }
    }

    [Fact]
    public async Task 停止序应为C1停再C3停()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            await controller.SetSpeedAsync(4.59);
            await controller.StartAsync();
            channel.Sent.Clear();

            await controller.StopAsync();

            var frames = Decode(channel.Sent);
            Assert.Equal(2, frames.Count);
            Assert.Equal(0xC1, frames[0].Command);
            Assert.Equal(0, frames[0].Data[0]);
            Assert.Equal(0xC3, frames[1].Command);
            Assert.Equal(0x54, frames[1].Data[0]);
            Assert.Equal(0, frames[1].Data[1]);
        }
    }

    [Fact]
    public async Task 换向应先停后转()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            channel.Sent.Clear();

            await controller.JogAsync(JogDirection.Up);
            var up = Assert.Single(Decode(channel.Sent));
            Assert.Equal(0xC1, up.Command);
            Assert.Equal(1, up.Data[0]);
            Assert.Equal(2400, (up.Data[1] << 8) | up.Data[2]); // JogSpeed 默认 2.4mm/min

            channel.Sent.Clear();
            await controller.JogAsync(JogDirection.Down);

            var frames = Decode(channel.Sent);
            Assert.Equal(2, frames.Count);
            Assert.Equal(0, frames[0].Data[0]); // 先停
            Assert.Equal(2, frames[1].Data[0]); // 再反转
        }
    }

    [Fact]
    public async Task 软件调零应只改偏移不发指令()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            await controller.SetSpeedAsync(4.59);
            await controller.StartAsync();
            channel.Pump(10.0);

            var sentBefore = channel.Sent.Count;
            await controller.ZeroDisplacementAsync();
            channel.Pump(10.0);

            Assert.Equal(sentBefore, channel.Sent.Count); // 调零不动设备

            TestSample? latest = null;
            controller.SampleReceived += (_, sample) => latest = sample;
            channel.Pump(0.1);

            Assert.NotNull(latest);
            Assert.Equal(0.773, latest.Value.Displacement, 0.01); // 净值从零点起算
            Assert.Equal(11.538, latest.Value.RawDisplacement, 0.01);
        }
    }

    [Fact]
    public async Task 施加围压应只发一条C2并等应答()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            channel.Sent.Clear();

            await controller.ApplyConfiningPressureAsync(60);

            // 单发：一条 C2 出去、拿到 A2 就完（稳压由下位机完成，不再轮询 / 补发）
            var frame = Assert.Single(Decode(channel.Sent));
            Assert.Equal(0xC2, frame.Command);
            Assert.Equal(20, frame.Data.Length);
            Assert.Equal(60, BinaryPrimitives.ReadInt32BigEndian(frame.Data.AsSpan(0))); // 围压目标 kPa
            Assert.Equal(0, BinaryPrimitives.ReadInt32BigEndian(frame.Data.AsSpan(4))); // 反压 0
            Assert.Equal(1, frame.Data[8]); // 围压启动
            Assert.Equal(0, frame.Data[9]); // 反压不启用（T1236 用不上）
            Assert.Equal(400, (frame.Data[10] << 8) | frame.Data[11]); // 40.0mm/min（现役 400）
            Assert.Equal(0, (frame.Data[12] << 8) | frame.Data[13]);
            Assert.Equal(50, (frame.Data[14] << 8) | frame.Data[15]); // 5.0mm/min（现役 50）
            Assert.Equal(19635u, BinaryPrimitives.ReadUInt32BigEndian(frame.Data.AsSpan(16))); // 19.635cm²
        }
    }

    [Fact]
    public async Task 卸除围压应发现役同帧全零C2()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            channel.Sent.Clear();

            await controller.ReleaseConfiningPressureAsync();

            var frame = Assert.Single(Decode(channel.Sent));
            Assert.Equal(0xC2, frame.Command);
            Assert.All(frame.Data[..16], b => Assert.Equal(0, b)); // 目标全 0 + 停止位 + 速度 0
            Assert.Equal(19635u, BinaryPrimitives.ReadUInt32BigEndian(frame.Data.AsSpan(16)));
        }
    }

    [Fact]
    public async Task 围压速度与加载杆面积应按属性换算成原始值()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            controller.ConfiningPressureSpeedMillimetersPerMinute = 25.0; // → 250
            controller.StableSpeedMillimetersPerMinute = 3.0; // → 30
            controller.LoadingRodAreaSquareCentimeters = 20.0; // → 20000
            channel.Sent.Clear();

            await controller.ApplyConfiningPressureAsync(100);

            var frame = Assert.Single(Decode(channel.Sent));
            Assert.Equal(100, BinaryPrimitives.ReadInt32BigEndian(frame.Data.AsSpan(0)));
            Assert.Equal(250, (frame.Data[10] << 8) | frame.Data[11]);
            Assert.Equal(30, (frame.Data[14] << 8) | frame.Data[15]);
            Assert.Equal(20000u, BinaryPrimitives.ReadUInt32BigEndian(frame.Data.AsSpan(16)));
        }
    }

    [Fact]
    public async Task 未联机时施加围压应拒绝_卸除静默跳过()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => controller.ApplyConfiningPressureAsync(30)
            );

            await controller.ReleaseConfiningPressureAsync(); // 收尾路径：不炸、不发帧

            Assert.Empty(channel.Sent);
            Assert.False(controller.IsConnected);
        }
    }

    [Fact]
    public async Task 荷载调零应只改偏移不发指令()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            await controller.SetSpeedAsync(4.59);
            await controller.StartAsync();
            channel.Pump(60.0); // 4.59mm × 0.16kN/mm ≈ 0.734kN

            TestSample latest = default;
            controller.SampleReceived += (_, sample) => latest = sample;
            channel.Pump(0.1);
            Assert.True(latest.Load > 0.7, $"调零前应有荷载，实际 {latest.Load}");

            var sentBefore = channel.Sent.Count;
            await controller.ZeroLoadAsync();
            channel.Pump(0.1);

            Assert.Equal(sentBefore, channel.Sent.Count); // 调零不动设备
            Assert.True(Math.Abs(latest.Load) < 0.05, $"调零后应从 0 起算，实际 {latest.Load}");
        }
    }

    [Fact]
    public async Task 采样应带围压值与稳压位()
    {
        var (controller, channel) = Create();
        await using (controller)
        {
            await controller.ConnectAsync();
            await controller.ApplyConfiningPressureAsync(60);

            TestSample latest = default;
            controller.SampleReceived += (_, sample) => latest = sample;
            channel.Pump(0.6); // 缓升 100kPa/s：0.6 秒正好到目标并置"稳压"

            Assert.Equal(60.0, latest.ConfiningPressureKPa!.Value, 1e-6);
            Assert.True(latest.ConfiningStable);
        }
    }

    [Fact]
    public async Task 等待应答超时应抛出并关闭通道()
    {
        var channel = new SilentChannel();
        var controller = new GeoDeviceController(
            channel,
            commandTimeout: TimeSpan.FromMilliseconds(100),
            reversalPause: TimeSpan.Zero
        );
        await using (controller)
        {
            await Assert.ThrowsAsync<TimeoutException>(() => controller.ConnectAsync());

            Assert.False(controller.IsConnected);
            Assert.False(channel.IsOpen);
        }
    }

    private static (GeoDeviceController Controller, RecordingChannel Channel) Create()
    {
        var channel = new RecordingChannel(new GeoSimulator());

        return (
            new GeoDeviceController(
                channel,
                commandTimeout: TimeSpan.FromMilliseconds(300),
                reversalPause: TimeSpan.Zero
            ),
            channel
        );
    }

    private static List<GeoFrame> Decode(IEnumerable<byte[]> sent)
    {
        var assembler = new GeoFrameAssembler();

        return [.. sent.SelectMany(bytes => assembler.Feed(bytes))];
    }
}
