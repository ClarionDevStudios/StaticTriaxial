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
            pressChannelIndex: 0,
            displacementSource: DisplacementSource.Displace2
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
            // contactGap 给 0：这一段是"回放 A2 原始帧"，位移就该从 0 起（真机路径里
            // 接触空行程体现在控制器积分出来的行程上）
            var sample = SampleCurve.At(seconds, 9.0, 5.0, contactGapMM: 0);
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
    /// 双传感器（两路平均）与保护口径：位移取两路平均值，**量程保护取两路里较大的那一路**
    /// （用户 2026-10-12 定的口径）。四种平均方式（位移1+2/3+4、千分表1+2/3+4）都由来源枚举决定。
    /// </summary>
    [Fact]
    public async Task 双传感器应取两路平均且保护按较大的那一路()
    {
        var channel = new FakeChannel();
        await using var controller = new Gr3DeviceController(channel)
        {
            DisplacementSource = DisplacementSource.DisplaceAverage12,
        };

        var samples = new List<TestSample>();
        controller.SampleReceived += (_, sample) => samples.Add(sample);

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        // 假 A0 的位移小数位 = 0 → 原始码值就是 mm；两路给 4.0 与 6.0
        channel.Feed(
            Gr3FrameCodec.BuildDatagram(
                Gr3Constants.UplinkRealtimeData,
                BuildDualDisplacePayload(4.0, 6.0),
                2
            )
        );

        Assert.Equal(5.0, samples[^1].Displacement, 6); // 平均值
        Assert.Equal(6.0, samples[^1].RawDisplacement, 6); // 较大的那一路

        // 单路来源（通道 1）时不平均，保护值就是它自己
        controller.DisplacementSource = DisplacementSource.Displace1;
        channel.Feed(
            Gr3FrameCodec.BuildDatagram(
                Gr3Constants.UplinkRealtimeData,
                BuildDualDisplacePayload(4.0, 6.0),
                3
            )
        );

        Assert.Equal(4.0, samples[^1].Displacement, 6);
        Assert.Equal(4.0, samples[^1].RawDisplacement, 6);
    }

    /// <summary>
    /// A2H 数据域（71 B）：位移**通道 1 / 2（索引 0 / 1）**给指定值 —— 1+2 平均（双传感器）用例用。
    /// 布局：压力 4×Int32（0~15）+ 围压（16~19）+ 位移 4×Int32（20~35）+ …
    /// </summary>
    private static byte[] BuildDualDisplacePayload(double channel1MM, double channel2MM)
    {
        var payload = BuildRealtimePayload(0, 0);
        WriteInt32(payload, 20, channel1MM);
        WriteInt32(payload, 24, channel2MM);
        return payload;
    }

    private static void WriteInt32(byte[] payload, int offset, double value)
    {
        var raw = (int)Math.Round(value);
        payload[offset] = (byte)(raw >> 24);
        payload[offset + 1] = (byte)(raw >> 16);
        payload[offset + 2] = (byte)(raw >> 8);
        payload[offset + 3] = (byte)raw;
    }

    /// <summary>
    /// 接触调零走**上位机软件调零**（存偏移量；用户 2026-10-12 明确按现役做法）：
    /// 调零后净值从 0 起算，**原始位移不变**（量程保护要用它）；且不动设备零点（C3 是另一条路）。
    /// </summary>
    [Fact]
    public async Task 接触调零应走软件偏移量且保留原始位移()
    {
        var channel = new FakeChannel();
        await using var controller = new Gr3DeviceController(channel);

        var samples = new List<TestSample>();
        controller.SampleReceived += (_, sample) => samples.Add(sample);

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        // 假 A0 的位移小数位 = 0 → 原始码值就是 mm
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkRealtimeData, BuildRealtimePayload(0.5, 5.0), 2));
        Assert.Equal(5.0, samples[^1].Displacement, 6);
        Assert.Equal(5.0, samples[^1].RawDisplacement, 6);

        // 接触成立 → 软件调零；此后净值 = 原始 − 偏移
        await controller.ZeroDisplacementAsync();
        Assert.Equal(5.0, controller.DisplacementOffsetMM, 6);

        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkRealtimeData, BuildRealtimePayload(0.5, 7.0), 3));
        Assert.Equal(2.0, samples[^1].Displacement, 6); // 净值
        Assert.Equal(7.0, samples[^1].RawDisplacement, 6); // 原始值不变

        // 只调位移：没有对设备发任何"清零"指令（C3 只有显式 ResetAsync 才发）
        Assert.DoesNotContain(
            channel.SentFrames,
            frame => frame[10] == Gr3Constants.DownlinkZero
        );
    }

    /// <summary>
    /// **换向时必须先发停止、再发启动**（用户 2026-10-12 明确要求）。封装层单一出口负责，
    /// 调用方（界面按住上升后改按下降、复位后重新启动…）不需要记得发停止。
    /// </summary>
    [Fact]
    public async Task 换向时应先发停止再发启动()
    {
        var channel = new FakeChannel { AutoAck = true };
        await using var controller = new Gr3DeviceController(channel)
        {
            DirectionChangeSettleDelay = TimeSpan.FromMilliseconds(10),
        };

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        var c4Orders = () =>
            channel
                .SentFrames.Where(frame => frame[10] == Gr3Constants.DownlinkMotorLoad)
                .Select(frame => frame[11])
                .ToArray();

        // 上升 → 同向再按上升：不该插停止
        await controller.JogAsync(JogDirection.Up);
        await controller.JogAsync(JogDirection.Up);
        Assert.Equal([Gr3Constants.MotorOrderForwardUp, Gr3Constants.MotorOrderForwardUp], c4Orders());

        // 上升中直接按下降：必须先发停止（0）再发反转（2）
        await controller.JogAsync(JogDirection.Down);
        Assert.Equal(
            [
                Gr3Constants.MotorOrderForwardUp,
                Gr3Constants.MotorOrderForwardUp,
                Gr3Constants.MotorOrderStop,
                Gr3Constants.MotorOrderReverseDown,
            ],
            c4Orders()
        );

        // 反转中启动试验（C4 正转）：同样先停后转
        await controller.StartAsync();
        var afterStart = c4Orders();
        Assert.Equal(Gr3Constants.MotorOrderStop, afterStart[^2]);
        Assert.Equal(Gr3Constants.MotorOrderForwardUp, afterStart[^1]);

        // 停止指令本身不会再插一条停止
        await controller.StopAsync();
        Assert.Equal(Gr3Constants.MotorOrderStop, c4Orders()[^1]);
    }

    /// <summary>
    /// 归位（现役 tpAutoReset）：**先停再反转**，且速率用调用方给的**试验速率**，
    /// 不是手动点动的平台升降速率（现役 SetVelocityCmd(2, LTestSpeed)）。
    /// </summary>
    [Fact]
    public async Task 归位应先停再反转并按给点速率下发()
    {
        var channel = new FakeChannel { AutoAck = true };
        await using var controller = new Gr3DeviceController(channel)
        {
            DirectionChangeSettleDelay = TimeSpan.FromMilliseconds(10),
            JogSpeedMillimetersPerMinute = 5, // 故意与试验速率不同：验证归位没误用点动速率
        };

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        await controller.StartAsync(); // 正转加载中
        await controller.ReturnToStartAsync(50); // 归位：先停，再反转下降

        var c4 = channel
            .SentFrames.Where(frame => frame[10] == Gr3Constants.DownlinkMotorLoad)
            .ToArray();

        Assert.Equal(Gr3Constants.MotorOrderStop, c4[^2][11]);
        Assert.Equal(Gr3Constants.MotorOrderReverseDown, c4[^1][11]);

        // 数据域：方向字(1B) + 速度(2B 大端)。50mm/min × 10 = 500 = 0x01F4
        Assert.Equal(0x01, c4[^1][12]);
        Assert.Equal(0xF4, c4[^1][13]);
    }

    /// <summary>
    /// 电机运转中改速率**必须重发 C4**（协议：同向运转时只有重发才改速度；现役 `SetVelocityCmd`
    /// 每次调用都重发）。ANX 之前只改本地字段 → "接触后切到试验速率"实际不生效，整轮按接近速率跑。
    /// </summary>
    [Fact]
    public async Task 电机运转中改速率应重发C4()
    {
        var channel = new FakeChannel { AutoAck = true };
        await using var controller = new Gr3DeviceController(channel);

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        // 电机没在转：只记速率，不发帧
        var sentBefore = channel.SentFrames.Count;
        await controller.SetSpeedAsync(50);
        Assert.Equal(sentBefore, channel.SentFrames.Count);

        // 机器状态报"正转运转中"（Bit3 = 0x08）
        channel.Feed(
            Gr3FrameCodec.BuildDatagram(
                Gr3Constants.UplinkRealtimeData,
                BuildRealtimePayload(0, 0, machineStatus: 0x08),
                2
            )
        );
        Assert.True(controller.IsMotorRunning);

        // 值没变：不重复下发
        await controller.SetSpeedAsync(50);
        Assert.Equal(sentBefore, channel.SentFrames.Count);

        // 值变了：按当前方向重发 C4（正转 = 1，30mm/min × 10 = 300 = 0x012C）
        await controller.SetSpeedAsync(30);

        var frame = channel.SentFrames[^1];
        Assert.Equal(Gr3Constants.DownlinkMotorLoad, frame[10]);
        Assert.Equal(Gr3Constants.MotorOrderForwardUp, frame[11]);
        Assert.Equal(0x01, frame[12]);
        Assert.Equal(0x2C, frame[13]);
    }

    /// <summary>
    /// 量程来自 A0H 的通道配置，且**轴向压力用压力通道 1（索引 0）** —— 依据现役生产库
    /// `TestParameterConfig.AxialPressureChannel = 0`（100.000kN 主传感器）。假 A0 里通道 1 = 1000/100 = 10.00kN。
    /// </summary>
    [Fact]
    public async Task 设备量程应取自A0的对应通道()
    {
        var channel = new FakeChannel();
        await using var controller = new Gr3DeviceController(channel);

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        Assert.Equal(10.0, controller.LoadRangeKN!.Value, 6); // 压力通道 1：量程 1000 / 10^2
        Assert.Equal(1000.0, controller.DisplacementRangeMillimeters!.Value, 6); // 位移通道 2：量程 1000 / 10^0
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
    /// 保活帧：下位机 60 秒收不到任何下行帧就回收会话（2026-10-11 真机实测：A2 流整段停掉、
    /// 机器一并停住），所以联机期间自动发 **CAH 读通道配置**（用户定的口径）；断开后要停。
    /// </summary>
    [Fact]
    public async Task 联机期间应周期性发保活帧断开后停止()
    {
        var channel = new FakeChannel();
        await using var controller = new Gr3DeviceController(channel)
        {
            KeepAliveInterval = TimeSpan.FromMilliseconds(30),
        };

        var connect = controller.ConnectAsync();
        channel.Feed(Gr3FrameCodec.BuildDatagram(Gr3Constants.UplinkSystemInfo, BuildSystemInfoPayload(), 1));
        await connect;

        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (
            channel.SentFrames.Count(frame => frame[10] == Gr3Constants.DownlinkReadChannelConfig) < 3
            && DateTime.UtcNow < deadline
        )
        {
            await Task.Delay(20);
        }

        var keepAlives = channel.SentFrames
            .Where(frame => frame[10] == Gr3Constants.DownlinkReadChannelConfig)
            .ToArray();
        Assert.True(keepAlives.Length >= 3, $"联机后 2 秒内只发了 {keepAlives.Length} 次保活帧");

        // 保活帧内容：CAH order=1（读通道配置）通道 1
        Assert.Equal(1, keepAlives[0][11]);
        Assert.Equal(1, keepAlives[0][12]);

        // 不再用 CBH：协议里 CB 叫"心跳保活"，但保活口径已改为 CAH
        Assert.DoesNotContain(channel.SentFrames, frame => frame[10] == Gr3Constants.DownlinkHeartbeat);

        await controller.DisconnectAsync();

        var sentAtDisconnect = channel.SentFrames.Count;
        await Task.Delay(200);
        Assert.DoesNotContain(
            channel.SentFrames.Skip(sentAtDisconnect),
            frame => frame[10] == Gr3Constants.DownlinkReadChannelConfig
        );
        Assert.Equal(sentAtDisconnect, channel.SentFrames.Count);
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
    private static byte[] BuildRealtimePayload(
        double loadKN,
        double displacementMM,
        byte machineStatus = 0,
        double secondDisplacement = 0
    )
    {
        var payload = new List<byte>();

        // 压力 4 通道：**通道 1（索引 0）**用 loadKN × 100（小数位 2），其余为 0 / 未接
        var rawPress = (int)Math.Round(loadKN * 100);
        foreach (var value in new[] { rawPress, 0, 0, 0 })
        {
            payload.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        }

        payload.AddRange([0, 0, 0, 0]); // 围压

        var rawDisplace = (int)Math.Round(displacementMM);
        var rawSecond = (int)Math.Round(secondDisplacement);
        foreach (var value in new[] { 0, rawDisplace, rawSecond, 0 })
        {
            payload.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
        }

        payload.AddRange(new byte[4 * 4]); // 微米 4 通道
        payload.AddRange([0, 0]); // 目标温度
        payload.AddRange([0, 0]); // 当前温度
        payload.Add(machineStatus); // 机器状态
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
