namespace ANX.Control.Tests;

using ANX.Communication.Channels;
using ANX.Core;
using ANX.Core.Communication;
using ANX.Core.Control;
using ANX.Protocol.Gr3;

/// <summary>
/// 完整装配的端到端：**编解码器 → 设备适配器 → 阶段机**。
/// 用"假通道"回放按真机布局拼出来的 A0 / A2 帧，验证原始码值能被换算成工程量、
/// 并驱动阶段机跑完一轮 —— 这条链不依赖真机与真实时间。
/// </summary>
public sealed class DeviceChainTests
{
    [Fact]
    public async Task 回放A0与A2帧应驱动阶段机跑完一轮()
    {
        var channel = new FakeChannel();
        await using var controller = new Gr3DeviceController(
            channel,
            pressChannelIndex: 1,
            displaceChannelIndex: 1
        );

        var sequencer = new TestSequencer(
            new TestRunProfile
            {
                ContactLoadKN = 0.05,
                BreakDropKN = 0.3,
                BreakConfirmSamples = 3,
                ResetSeconds = 0.5,
                MaxLoadKN = 50,
                MaxDisplacementMM = 20,
            }
        );
        sequencer.Start();
        controller.SampleReceived += (_, sample) => sequencer.OnSample(sample);

        // ① 联机：C0 之后设备自动推 A0
        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        Assert.True(controller.IsConnected);
        // 压力通道 1：量程 1000、小数位 2、量纲 1(kN) → 原始 900 应为 9.00 kN
        Assert.Equal(2, controller.SystemInfo!.PressChannels[1].Digital);

        // ② 回放一段 A2：模拟器的采样折算回原始码值（kN×100、mm×1）
        var sequence = (ushort)2;
        var seconds = 0.0;
        var finished = false;

        while (seconds <= 30.0 && !finished)
        {
            var sample = SampleCurve.At(seconds, 9.0, 5.0);
            controller.ElapsedSeconds = seconds;
            channel.Feed(
                Gr3FrameCodec.BuildDatagram(
                    Gr3Constants.UplinkRealtimeData,
                    BuildRealtimePayload(sample.Load, sample.Displacement),
                    sequence++
                )
            );

            finished = sequencer.IsFinished;
            seconds += 0.02;
        }

        Assert.True(finished, "回放的 A2 帧应能把试验跑完");
        Assert.Equal(TestStopReason.Completed, sequencer.StopReason);
        Assert.True(sequencer.CanSettle);
        Assert.True(sequencer.Curve.Count > 100, $"曲线点太少：{sequencer.Curve.Count}");
        Assert.Equal(9.0, sequencer.Peak.Load, 0.01); // 换算后的工程量
        Assert.Equal(5.0, sequencer.Peak.Displacement, 0.01);
    }

    [Fact]
    public async Task 复位应发C3清零停止应发C4方向S()
    {
        var channel = new FakeChannel();
        await using var controller = new Gr3DeviceController(channel);

        // 先联机（订阅在 ConnectAsync 里建立；A0 由设备自动推）
        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        var reset = controller.ResetAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkAck, [Gr3Constants.AckSuccess], 2));
        await reset;

        // 帧内命令字固定在外层偏移 10
        Assert.Equal(Gr3Constants.DownlinkZero, channel.SentFrames[^1][10]);

        var stop = controller.StopAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkAck, [Gr3Constants.AckSuccess], 3));
        await stop;

        var stopFrame = channel.SentFrames[^1];
        Assert.Equal(Gr3Constants.DownlinkMotorLoad, stopFrame[10]);
        // 数据域紧跟在命令字之后（偏移 11）
        // 方向字：0 = 电机停止（用户 2026-10-10 给出的定义）
        Assert.Equal(Gr3Constants.MotorOrderStop, stopFrame[11]);
    }

    [Fact]
    public async Task 方向字应为零停一正转上升二反转下降()
    {
        var channel = new FakeChannel { AutoAck = true };
        await using var controller = new Gr3DeviceController(channel);

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        await controller.StartAsync();

        // 启动 = C1 试验模式 + C4 正转上升
        Assert.Equal(Gr3Constants.DownlinkTestMode, channel.SentFrames[^2][10]);
        Assert.Equal(Gr3Constants.DownlinkMotorLoad, channel.SentFrames[^1][10]);
        Assert.Equal(Gr3Constants.MotorOrderForwardUp, channel.SentFrames[^1][11]);

        // 方向字取值：0 停止 / 1 正转上升 / 2 反转下降
        Assert.Equal(0, Gr3Constants.MotorOrderStop);
        Assert.Equal(1, Gr3Constants.MotorOrderForwardUp);
        Assert.Equal(2, Gr3Constants.MotorOrderReverseDown);
    }

    /// <summary>
    /// 速度字段必须按现役的换算下发（现役 GR3CommandDispatcher：下发值 = 速率 × 10）。
    /// 2026-10-11 真机空载试发现：直接把 mm/min 当字段值发，机器只有预期速率的零头。
    /// </summary>
    [Fact]
    public async Task 速度字段应按现役换算下发()
    {
        var channel = new FakeChannel { AutoAck = true };
        await using var controller = new Gr3DeviceController(channel);

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        await controller.JogAtSpeedAsync(JogDirection.Up, 50);

        var frame = channel.SentFrames[^1];
        Assert.Equal(Gr3Constants.DownlinkMotorLoad, frame[10]);
        Assert.Equal(Gr3Constants.MotorOrderForwardUp, frame[11]);

        // 数据域：方向字(1B) + 速度(2B 大端)。50mm/min × 10 = 500 = 0x01F4
        Assert.Equal(0x01, frame[12]);
        Assert.Equal(0xF4, frame[13]);

        // 超上限一律截断：现役的点动倍率（试验速率 × 10）会算出 500mm/min，落在这里就是 500 而不是 5000
        Assert.Equal(500, Gr3MotorSpeed.ToWire(500));
    }

    [Fact]
    public void 速率换算边界()
    {
        Assert.Equal(500, Gr3MotorSpeed.ToWire(50));
        Assert.Equal(100, Gr3MotorSpeed.ToWire(10));
        Assert.Equal(0, Gr3MotorSpeed.ToWire(0));
        Assert.Equal(0, Gr3MotorSpeed.ToWire(-5));

        // 电机上限 50mm/min：超了伺服会宕机（用户 2026-10-11），所以一律截断到 500
        Assert.Equal(50.0, Gr3MotorSpeed.MaxMillimetersPerMinute);
        Assert.Equal(500, Gr3MotorSpeed.ToWire(60));
        Assert.Equal(500, Gr3MotorSpeed.ToWire(100_000));

        Assert.Equal(50.0, Gr3MotorSpeed.FromWire(500), 6);
    }

    /// <summary>
    /// A0H 数据域（56 B）：版本 + 压力通道量程(4×Int32) + 位移通道量程(4×Int32) +
    /// 压力小数位(4) + 位移小数位(4) + 压力量纲(4) + 位移量纲(4) + 微米有效(4) + 围压有效(1)。
    /// </summary>
    private static byte[] BuildSystemInfoPayload()
    {
        var payload = new List<byte> { 10, 15, 10 };

        foreach (var scale in new[] { 1000, 1000, 300, 0 })
        {
            payload.AddRange([(byte)(scale >> 24), (byte)(scale >> 16), (byte)(scale >> 8), (byte)scale]);
        }

        foreach (var scale in new[] { 1000, 1000, 0, 0 })
        {
            payload.AddRange([(byte)(scale >> 24), (byte)(scale >> 16), (byte)(scale >> 8), (byte)scale]);
        }

        payload.AddRange([2, 2, 3, 0]); // 压力小数位
        payload.AddRange([0, 0, 0, 0]); // 位移小数位
        payload.AddRange([1, 0, 3, 0]); // 压力量纲
        payload.AddRange([4, 4, 0, 0]); // 位移量纲（mm）
        payload.AddRange([0, 0, 0, 0]); // 微米有效
        payload.Add(1); // 围压有效

        Assert.Equal(Gr3Constants.SystemInfoLength, payload.Count);
        return [.. payload];
    }

    /// <summary>A2H 数据域（71 B）：按工程量换算回原始码值。</summary>
    private static byte[] BuildRealtimePayload(double loadKN, double displacementMM)
    {
        var payload = new List<byte>();

        // 压力 4 通道：通道 1 用 loadKN × 100（小数位 2），其余为 0 / 未接
        var rawPress = (int)Math.Round(loadKN * 100);
        foreach (var value in new[] { 0, rawPress, 0, 0 })
        {
            payload.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        }

        payload.AddRange([0, 0, 0, 0]); // 围压

        var rawDisplace = (int)Math.Round(displacementMM);
        foreach (var value in new[] { 0, rawDisplace, 0, 0 })
        {
            payload.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        }

        payload.AddRange(new byte[4 * 4]); // 微米 4 通道
        payload.AddRange([0, 0]); // 目标温度
        payload.AddRange([0, 0]); // 当前温度
        payload.Add(0); // 机器状态
        payload.Add(0); // 试验状态
        payload.AddRange([0, 0]); // 端口输出 B
        payload.Add(0); // 端口输入 A
        payload.AddRange([0, 0]); // 端口输入 B
        payload.AddRange(new byte[8]); // 扩展引伸计

        Assert.Equal(Gr3Constants.RealtimeDataLength, payload.Count);
        return [.. payload];
    }

    /// <summary>只回放的假通道：不真发数据，由测试主动 Feed。</summary>
    private sealed class FakeChannel : ICommChannel
    {
        public ChannelKind Kind => ChannelKind.Simulator;

        public string DisplayName => "假通道";

        public bool IsOpen { get; private set; }

        public event EventHandler<ReceivedChunk>? DataReceived;

        /// <summary>记录下发的帧，供断言命令字。</summary>
        public List<byte[]> SentFrames { get; } = [];

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

        public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            SentFrames.Add(data.ToArray());

            if (AutoAck)
            {
                // 像真设备一样立刻回 A1 成功，便于测多命令流程
                DataReceived?.Invoke(
                    this,
                    new ReceivedChunk(
                        Gr3FrameCodec.BuildDatagram(
                            Gr3Constants.UplinkAck,
                            [Gr3Constants.AckSuccess],
                            (ushort)SentFrames.Count
                        )
                    )
                );
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Feed(byte[] frame) => DataReceived?.Invoke(this, new ReceivedChunk(frame));

        /// <summary>为真时，每次下发后自动回一帧 A1 成功。</summary>
        public bool AutoAck { get; set; }
    }
}
