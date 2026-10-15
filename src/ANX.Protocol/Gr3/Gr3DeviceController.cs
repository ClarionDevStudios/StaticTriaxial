namespace ANX.Protocol.Gr3;

using ANX.Core;
using ANX.Core.Communication;
using ANX.Core.Control;
using ANX.Core.Protocol;

/// <summary>
/// GR-3 设备适配器：把 <see cref="ICommChannel"/> + <see cref="Gr3FrameCodec"/> 接到
/// <see cref="ITestDeviceController"/> 上。框架只认"联机 / 设速 / 启动 / 停止 / 复位 / 采样快照"，
/// 机型命令全部留在这里。
/// </summary>
/// <remarks>
/// 命令归属（2026-10-10 用户澄清）：**复位 = C3H 清零**；C5H 是**试验控制指令**（协议目录把它写成
/// "电机控制命令"）；C4H 是电机加载 / 停止。
/// 方向字（2026-10-11 真机空载试已验证）：0 停 / 1 正转上升 / 2 反转下降 —— 分别读到机器状态
/// 0x00 / 0x08 / 0x10，C3H 清零后位移由 0.0180mm 回到 0.0000mm。
/// 速度字段的单位换算见 <see cref="Gr3MotorSpeed"/>（现役：下发值 = 速率 × 10）。
/// </remarks>
public sealed class Gr3DeviceController : ITestDeviceController, IAsyncDisposable
{
    private readonly ICommChannel _channel;
    private readonly Gr3FrameCodec _codec = new();
    private int _pressChannelIndex;
    private DisplacementSource _displacementSource = DisplacementSource.Displace2;
    private readonly TimeSpan _timeout;

    private TaskCompletionSource<Gr3AckStatus>? _ackWaiter;
    private TaskCompletionSource<bool>? _systemInfoWaiter;
    private bool _subscribed;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatLoop;
    private byte _lastCommandedMotorOrder;
    private double _displacementOffsetMM;

    /// <summary>
    /// 接触调零的偏移量（mm，上位机侧）。**净值 = 原始位移 − 本偏移**；不动设备零点 ——
    /// 设备侧的硬件清零是 C3（<see cref="ResetAsync"/>），是另一件事。
    /// 口径依据现役 `AnalyzeDataController.ZeroChannel`（`ZeroPoint := 当前值`，净值 = 当前 − 零点）。
    /// </summary>
    public double DisplacementOffsetMM => _displacementOffsetMM;

    /// <summary>
    /// 接触调零（上位机软件调零，用户 2026-10-12 明确：按现役做法、存偏移量）。
    /// 把**当前位移读数**记为偏移量，之后的采样净值为 0 起算；每次接触都会重新记一次。
    /// 只调位移，不调荷载（与现役 `ZeroAxialDisplacement` 一致）。
    /// </summary>
    public Task ZeroDisplacementAsync(CancellationToken cancellationToken = default)
    {
        _displacementOffsetMM = LastDisplacementMM;
        return Task.CompletedTask;
    }

    /// <summary>最近一帧的位移原始读数（mm）；还没收到过 A2 时为 0。</summary>
    private double LastDisplacementMM { get; set; }

    /// <summary>
    /// **换向时**先发停止、再发启动之间要等的时间（默认 500ms，等伺服停稳）。
    /// 用户 2026-10-12 明确要求："电机运动换向时，先发电机停止命令，再发送电机启动命令"。
    /// </summary>
    public TimeSpan DirectionChangeSettleDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    public Gr3DeviceController(
        ICommChannel channel,
        int pressChannelIndex = 0,
        DisplacementSource displacementSource = DisplacementSource.Displace2,
        TimeSpan? timeout = null
    )
    {
        _channel = channel;
        _pressChannelIndex = pressChannelIndex;
        _displacementSource = displacementSource;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
    }

    public string DeviceName => $"GR-3（{_channel.DisplayName}）";

    public bool IsConnected => SystemInfo is not null;

    /// <summary>
    /// 位移通道量程（mm）= A0H 里该通道的"量程 / 10^小数位"。真机 2026-10-11 实测 25.000mm，
    /// 而位移闸门当时写的是 50mm（比传感器量程宽一倍）—— 靠操作员手动停机才没顶坏传感器。
    /// </summary>
    public double? DisplacementRangeMillimeters =>
        SystemInfo is { } info
        && _displacementSource.Channels().First < info.DisplaceChannels.Count
            ? info.DisplaceChannels[_displacementSource.Channels().First].ToEngineeringValue(
                info.DisplaceChannels[_displacementSource.Channels().First].Scale
            )
            : null;

    /// <summary>
    /// 压力通道量程（kN，轴向压力用）= A0H 里该通道的"量程 / 10^小数位"。
    /// **轴向压力通道 = 索引 0（压力通道 1）** —— 依据现役生产库 `TestParameterConfig`：
    /// 弯曲（AsphaltTestTypeID=5）等所有配置行都是 `AxialPressureChannel = 0`，即 100.000kN 主传感器；
    /// 索引 1 是 5.000kN 的小量程通道（ANX 2026-10-12 之前误用了它）。
    /// </summary>
    public double? LoadRangeKN =>
        SystemInfo is { } info
        && _pressChannelIndex >= 0
        && _pressChannelIndex < info.PressChannels.Count
            ? info.PressChannels[_pressChannelIndex].ToEngineeringValue(
                info.PressChannels[_pressChannelIndex].Scale
            )
            : null;

    /// <summary>电机是否在运转（机器状态位 Bit3 正转 / Bit4 反转）。未联机时为 null。</summary>
    public bool? IsMotorRunning =>
        IsConnected
            ? (LastMachineStatus & (Gr3MachineStatus.MotorForward | Gr3MachineStatus.MotorReverse)) != 0
            : null;

    /// <summary>A0H 系统配置（决定原始码值怎么换算成工程量）。</summary>
    public Gr3A0SystemInfo? SystemInfo { get; private set; }

    /// <summary>A4H 设备信息。</summary>
    public Gr3DeviceInfo? DeviceInfo { get; private set; }

    /// <summary>最近一帧 A2H 的控制器状态寄存器（用于观察电机 / 限位 / 控温位）。</summary>
    public Gr3MachineStatus LastMachineStatus { get; private set; }

    /// <summary>最近一帧 A2H 的试验状态寄存器。</summary>
    public Gr3TestStatus LastTestStatus { get; private set; }

    public event EventHandler<TestSample>? SampleReceived;

    /// <summary>
    /// 当前试验已进行的秒数。**由控制循环每拍推进**（A2H 帧本身不带时间戳），
    /// 控制器只负责把它带进采样快照。
    /// </summary>
    public double ElapsedSeconds { get; set; }

    /// <summary>
    /// A2H 原始码值 → 采样快照：荷载按压力通道、位移按位移通道，
    /// 用 A0 给出的 `raw / 10^Digital` 换算（口径已由通道 1 的标定表反推证实）。
    /// A0 未到时**不做换算**，直接回原始值并置 `InfoReady = false`（此时数据不可信）。
    /// </summary>
    public static TestSample ToSample(
        Gr3A2RealtimeData data,
        Gr3A0SystemInfo? info,
        int pressChannelIndex,
        DisplacementSource displaceSource,
        double elapsedSeconds
    )
    {
        var pressRaw = data.PressData[pressChannelIndex];

        var load = info is null
            ? pressRaw
            : info.PressChannels[pressChannelIndex].ToEngineeringValue(pressRaw);

        // 位移来源：单路 / 双路平均（用户在通道设置里选；双传感器 = 两路平均）。
        // 保护口径按用户 2026-10-12 的要求取**两路里较大的那一路**（RawDisplacement 带出去给量程闸门）。
        var (first, second) = displaceSource.Channels();
        var micrometer = displaceSource.IsMicrometer();
        var average = displaceSource.IsAverage();

        var firstRaw = ReadRaw(data, info, micrometer, first);
        var secondRaw = ReadRaw(data, info, micrometer, second);

        var displacement = average ? (firstRaw + secondRaw) / 2.0 : firstRaw;
        var guardDisplacement = Math.Max(firstRaw, secondRaw);

        return new TestSample(load, displacement, elapsedSeconds)
        {
            // 量程保护用：两路里**较大**的那一路（单路来源即它自己）
            RawDisplacement = guardDisplacement,
        };

        // 局部函数：按来源读一路（位移通道用 A0 的通道换算；千分表按现役口径 原始值/1000 = mm）
        static double ReadRaw(
            Gr3A2RealtimeData data,
            Gr3A0SystemInfo? info,
            bool micrometer,
            int index
        )
        {
            if (micrometer)
            {
                var value = data.MicrometerData[index];
                return value / 1000.0;
            }

            var raw = data.DisplaceData[index];
            return info is null
                ? raw
                : info.DisplaceChannels[index].ToEngineeringValue(raw);
        }
    }

    /// <summary>把一帧上行指令喂给控制器（供接收回路与测试复用）。</summary>
    public void HandleFrame(ANX.Core.Protocol.DecodedFrame frame)
    {
        switch (frame.CommandCode)
        {
            case Gr3Constants.UplinkSystemInfo when Gr3UplinkParser.TryParseSystemInfo(frame, out var info):
                SystemInfo = info;
                _systemInfoWaiter?.TrySetResult(true);
                break;

            case Gr3Constants.UplinkDeviceId when Gr3UplinkParser.TryParseDeviceInfo(frame, out var device):
                DeviceInfo = device;
                break;

            case Gr3Constants.UplinkAck when Gr3UplinkParser.TryParseAck(frame, out var status):
                _ackWaiter?.TrySetResult(status);
                break;

            case Gr3Constants.UplinkRealtimeData when Gr3UplinkParser.TryParseRealtimeData(frame, out var data):
                LastMachineStatus = data.MachineStatus;
                LastTestStatus = data.TestStatus;
                var raw = ToSample(data, SystemInfo, _pressChannelIndex, DisplacementSource, ElapsedSeconds);
                LastDisplacementMM = raw.Displacement;
                SampleReceived?.Invoke(
                    this,
                    raw with
                    {
                        // 接触调零（上位机软件偏移）：净值 = 原始 − 偏移；原始值一并带出去供量程保护判
                        Displacement = raw.Displacement - _displacementOffsetMM,
                    }
                );
                break;
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Subscribe();

        // 联机前**重建一次通道**（关了再开）：接收循环若因任何原因退出过，只有重开才能自愈。
        // 2026-10-14 实测的坑：开机时网线没插 → 发 C0 收到 ICMP 不可达 → 接收循环当"已关闭"退出 →
        // 之后插上线也永远等不到 A0（联机一直超时）；而**新进程**握手一次就成功，证明设备与网络没问题。
        // 接收循环本身也已改成不因这类暂时性错误退出（见 UdpChannel），这里是第二道保险。
        await _channel.CloseAsync().ConfigureAwait(false);
        await _channel.OpenAsync(cancellationToken).ConfigureAwait(false);

        // C0 联机：设备接受后会**自动推 A0**（真机实测），所以这里等 A0 而不是等 A1。
        _systemInfoWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(Gr3DownlinkCommands.Connect(DateTime.Now), cancellationToken).ConfigureAwait(false);

        var completed = await Task
            .WhenAny(_systemInfoWaiter.Task, Task.Delay(_timeout, cancellationToken))
            .ConfigureAwait(false);

        if (completed != _systemInfoWaiter.Task)
        {
            throw LocalizedException.Single(
                ProtocolMessageKeys.ConnectTimeout,
                $"联机超时：{_timeout.TotalSeconds:0.#}s 内未收到 A0 系统配置",
                $"{_timeout.TotalSeconds:0.#}"
            );
        }

        StartKeepAlive();
    }

    public async Task DisconnectAsync()
    {
        await StopKeepAliveAsync().ConfigureAwait(false);
        Unsubscribe();
        await _channel.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 保活帧周期：**联机期间每 10 秒发一次 CAH 读通道配置**（用户 2026-10-11 定的口径）。
    /// 下位机 **60 秒**收不到任何下行帧就回收会话（真机实测，表现为 A2 流整段停掉、机器一并停住），
    /// 10 秒有 6 倍余量。测试可改小。
    /// </summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(10);

    public double SpeedMillimetersPerMinute { get; private set; } = 50.0;

    /// <summary>
    /// 手动点动（工作台上升 / 下降按钮）用的速率，mm/min。
    /// 默认 50mm/min = 电机上限（见 <see cref="Gr3MotorSpeed.MaxMillimetersPerMinute"/>）。
    /// **不照搬现役的点动倍率**：现役点动 = 试验速率 × 10（弯曲 50mm/min 参数 → 500mm/min），
    /// 超过 50mm/min 的硬上限会让伺服宕机，所以这里固定在上限内。
    /// </summary>
    public double JogSpeedMillimetersPerMinute { get; set; } = 50.0;

    public async Task SetSpeedAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    )
    {
        var changed = Math.Abs(SpeedMillimetersPerMinute - millimetersPerMinute) > 0.0001;
        SpeedMillimetersPerMinute = millimetersPerMinute;

        // 协议语义（用户 2026-10-10 给的 C4 说明）：**同向运转时只有重发电机指令才改速度**。
        // 现役 `SetVelocityCmd` 每次调用都重发 C4；ANX 之前只改了本地字段、没重发 ——
        // 于是接触后"切到试验速率"实际不生效，整轮都按接近速率跑。
        var direction =
            LastMachineStatus.HasFlag(Gr3MachineStatus.MotorForward) ? Gr3Constants.MotorOrderForwardUp
            : LastMachineStatus.HasFlag(Gr3MachineStatus.MotorReverse) ? Gr3Constants.MotorOrderReverseDown
            : Gr3Constants.MotorOrderStop;

        if (!changed || direction == Gr3Constants.MotorOrderStop)
        {
            return; // 值没变、或电机没在转：速率留在字段里，下次 Start / Jog 时带上
        }

        await SendMotorAsync(direction, Gr3MotorSpeed.ToWire(millimetersPerMinute), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 点动工作台（C4H）：方向字 1=正转上升 / 2=反转下降，速率取 <see cref="JogSpeedMillimetersPerMinute"/>。
    /// 执行页的"工作台上升 / 下降"按钮走这里。
    /// </summary>
    public Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default) =>
        JogAtSpeedAsync(direction, JogSpeedMillimetersPerMinute, cancellationToken);

    /// <summary>
    /// 归位（现役 tpAutoReset）：反转下降，速率用调用方给的**试验速率**。
    /// 换向由 <see cref="SendMotorAsync"/> 统一处理成"先停再反转"。
    /// </summary>
    public Task ReturnToStartAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) => JogAtSpeedAsync(JogDirection.Down, millimetersPerMinute, cancellationToken);

    /// <summary>
    /// 点动，显式指定速率（mm/min）。标定 / 需要精确速率时用，不加点动倍率。
    /// </summary>
    public Task JogAtSpeedAsync(
        JogDirection direction,
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) =>
        SendMotorAsync(
            direction == JogDirection.Up
                ? Gr3Constants.MotorOrderForwardUp
                : Gr3Constants.MotorOrderReverseDown,
            Gr3MotorSpeed.ToWire(millimetersPerMinute),
            cancellationToken
        );

    /// <summary>启动：先 C1 进试验模式（选通道），再 C4 让电机上升加载。</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ElapsedSeconds = 0;

        await SendAndWaitAckAsync(
                Gr3DownlinkCommands.TestMode(
                    function: 1,
                    recordMode: 1,
                    recordInterval: 100,
                    recordStart: 0,
                    pressChannel: (byte)(_pressChannelIndex + 1),
                    displaceChannel: (byte)DisplaceChannelDisplayNumber
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        await SendMotorAsync(
                Gr3Constants.MotorOrderForwardUp,
                Gr3MotorSpeed.ToWire(SpeedMillimetersPerMinute),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>停止电机（C4 方向字 0）。</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        SendMotorAsync(Gr3Constants.MotorOrderStop, 0, cancellationToken);

    /// <summary>
    /// 电机指令的**唯一出口**。换向（新方向 ≠ 当前运转方向）时按用户 2026-10-12 的要求
    /// **先发停止、等 <see cref="DirectionChangeSettleDelay"/>、再发新方向** —— 不靠调用方记得。
    /// </summary>
    private async Task SendMotorAsync(byte order, ushort speed, CancellationToken cancellationToken)
    {
        var current = CurrentMotorOrder;

        if (
            order != Gr3Constants.MotorOrderStop
            && current != Gr3Constants.MotorOrderStop
            && current != order
        )
        {
            await SendAndWaitAckAsync(
                    Gr3DownlinkCommands.MotorLoad(Gr3Constants.MotorOrderStop, 0),
                    cancellationToken
                )
                .ConfigureAwait(false);
            _lastCommandedMotorOrder = Gr3Constants.MotorOrderStop;

            await Task.Delay(DirectionChangeSettleDelay, cancellationToken).ConfigureAwait(false);
        }

        await SendAndWaitAckAsync(Gr3DownlinkCommands.MotorLoad(order, speed), cancellationToken)
            .ConfigureAwait(false);
        _lastCommandedMotorOrder = order;
    }

    /// <summary>
    /// 当前电机运转方向（0 停 / 1 正转上升 / 2 反转下降）：优先看机器状态位（设备是权威），
    /// 没有状态位时回落到"最近一条命令"。
    /// </summary>
    private byte CurrentMotorOrder =>
        LastMachineStatus.HasFlag(Gr3MachineStatus.MotorForward) ? Gr3Constants.MotorOrderForwardUp
        : LastMachineStatus.HasFlag(Gr3MachineStatus.MotorReverse) ? Gr3Constants.MotorOrderReverseDown
        : _lastCommandedMotorOrder;

    /// <summary>复位 / 清零（C3H，用户 2026-10-10 澄清：复位发 C3，不是 C5）。</summary>
    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        SendAndWaitAckAsync(Gr3DownlinkCommands.Zero(0x80, 0xFF), cancellationToken);

    /// <summary>
    /// CBH 心跳。协议把 CB 叫"心跳保活"，但**联机期间的自动保活用的是 CAH**
    /// （见 <see cref="SendKeepAliveAsync"/>，用户 2026-10-11 定的口径）；本方法留着做显式心跳 /
    /// 对照实验用（只读探针的 `--keepalive cb` 走这条）。
    /// </summary>
    public Task HeartbeatAsync(CancellationToken cancellationToken = default) =>
        SendAndWaitAckAsync(Gr3DownlinkCommands.Heartbeat(), cancellationToken);

    /// <summary>
    /// 保活帧：**CAH 读通道配置**（用户 2026-10-11 定的口径 —— 联机期间每 10 秒发一次 CAH）。
    /// 下位机 60 秒收不到任何下行帧就回收会话（真机实测），所以必须周期性发；用 CAH 而不是 CBH
    /// 还顺带读了通道配置（A3 不参与解析，不会干扰业务帧）。
    /// </summary>
    public Task SendKeepAliveAsync(CancellationToken cancellationToken = default) =>
        SendAsync(
            Gr3DownlinkCommands.ReadChannelConfig(1, KeepAliveChannelId),
            cancellationToken
        );

    /// <summary>保活帧读哪个通道的配置（协议规定 1-12；用固定 1，避开"通道不存在"被拒）。</summary>
    public byte KeepAliveChannelId { get; set; } = 1;

    /// <summary>轴向压力通道的**协议通道号**（显示用；协议通道 1 基，索引 0 基）。</summary>
    public int PressChannelDisplayNumber => _pressChannelIndex + 1;

    /// <summary>轴向位移通道的协议通道号（显示用）。</summary>
    public int DisplaceChannelDisplayNumber => _displacementSource.Channels().First + 1;

    /// <summary>
    /// 轴向压力通道号（1 基，1~4）—— 参数表「通道设置」页的取值经这里落到采样换算上。
    /// 依据现役生产库：弯曲等所有方法默认通道 1（100.000kN 主传感器），通道 2 是 5kN 小量程。
    /// </summary>
    public int PressChannelNumber
    {
        get => _pressChannelIndex + 1;
        set => _pressChannelIndex = Math.Clamp(value, 1, 4) - 1;
    }

    /// <summary>轴向位移通道号（1 基，1~4）。</summary>
    public DisplacementSource DisplacementSource
    {
        get => _displacementSource;
        set => _displacementSource = value;
    }

    /// <summary>联机成功后启动保活循环（幂等）。</summary>
    private void StartKeepAlive()
    {
        if (_heartbeatLoop is not null)
        {
            return;
        }

        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;

        _heartbeatLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(KeepAliveInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    // 只发不等应答：应答通道（_ackWaiter）是"一条命令一个应答"，保活帧不能去抢它
                    await SendKeepAliveAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 断开 / 释放时的正常停机
            }
            catch (Exception)
            {
                // 保活是尽力而为：发不出去也不打断业务，链路真断了会先由数据流表现出来
            }
        });
    }

    private async Task StopKeepAliveAsync()
    {
        if (_heartbeatCts is null)
        {
            return;
        }

        await _heartbeatCts.CancelAsync().ConfigureAwait(false);
        if (_heartbeatLoop is not null)
        {
            await _heartbeatLoop.ConfigureAwait(false);
        }

        _heartbeatCts.Dispose();
        _heartbeatCts = null;
        _heartbeatLoop = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopKeepAliveAsync().ConfigureAwait(false);
        Unsubscribe();
        await _channel.DisposeAsync().ConfigureAwait(false);
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _channel.DataReceived += OnDataReceived;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _channel.DataReceived -= OnDataReceived;
        _subscribed = false;
    }

    private void OnDataReceived(object? sender, ReceivedChunk chunk)
    {
        var buffer = chunk.Data.Span;

        while (Gr3FrameCodec.TryDecodeFrame(buffer, out var frame, out var consumed))
        {
            HandleFrame(frame);
            buffer = buffer[consumed..];
        }
    }

    private Task SendAsync(ICommand command, CancellationToken cancellationToken)
    {
        var frame = _codec.Encode(command);
        return _channel.SendAsync(frame, cancellationToken).AsTask();
    }

    private async Task SendAndWaitAckAsync(ICommand command, CancellationToken cancellationToken)
    {
        _ackWaiter = new TaskCompletionSource<Gr3AckStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(command, cancellationToken).ConfigureAwait(false);

        var completed = await Task
            .WhenAny(_ackWaiter.Task, Task.Delay(_timeout, cancellationToken))
            .ConfigureAwait(false);

        if (completed != _ackWaiter.Task)
        {
            throw LocalizedException.Single(
                ProtocolMessageKeys.AckTimeout,
                $"等待 A1 应答超时（命令 0x{command.CommandCode:X2}）",
                $"0x{command.CommandCode:X2}"
            );
        }

        var status = await _ackWaiter.Task.ConfigureAwait(false);
        if (status != Gr3AckStatus.Success)
        {
            throw LocalizedException.Single(
                ProtocolMessageKeys.CommandRejected,
                $"设备拒绝命令 0x{command.CommandCode:X2}：{status}",
                $"0x{command.CommandCode:X2}",
                status
            );
        }
    }
}
