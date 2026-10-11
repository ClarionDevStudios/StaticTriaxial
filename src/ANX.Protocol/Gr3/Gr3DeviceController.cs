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
    private readonly int _pressChannelIndex;
    private readonly int _displaceChannelIndex;
    private readonly TimeSpan _timeout;

    private TaskCompletionSource<Gr3AckStatus>? _ackWaiter;
    private TaskCompletionSource<bool>? _systemInfoWaiter;
    private bool _subscribed;
    private CancellationTokenSource? _heartbeatCts;
    private Task? _heartbeatLoop;

    public Gr3DeviceController(
        ICommChannel channel,
        int pressChannelIndex = 1,
        int displaceChannelIndex = 1,
        TimeSpan? timeout = null
    )
    {
        _channel = channel;
        _pressChannelIndex = pressChannelIndex;
        _displaceChannelIndex = displaceChannelIndex;
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
        && _displaceChannelIndex >= 0
        && _displaceChannelIndex < info.DisplaceChannels.Count
            ? info.DisplaceChannels[_displaceChannelIndex].ToEngineeringValue(
                info.DisplaceChannels[_displaceChannelIndex].Scale
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
        int displaceChannelIndex,
        double elapsedSeconds
    )
    {
        var pressRaw = data.PressData[pressChannelIndex];
        var displaceRaw = data.DisplaceData[displaceChannelIndex];

        var load = info is null
            ? pressRaw
            : info.PressChannels[pressChannelIndex].ToEngineeringValue(pressRaw);
        var displacement = info is null
            ? displaceRaw
            : info.DisplaceChannels[displaceChannelIndex].ToEngineeringValue(displaceRaw);

        return new TestSample(load, displacement, elapsedSeconds);
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
                SampleReceived?.Invoke(
                    this,
                    ToSample(data, SystemInfo, _pressChannelIndex, _displaceChannelIndex, ElapsedSeconds)
                );
                break;
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Subscribe();

        if (!_channel.IsOpen)
        {
            await _channel.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        // C0 联机：设备接受后会**自动推 A0**（真机实测），所以这里等 A0 而不是等 A1。
        _systemInfoWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await SendAsync(Gr3DownlinkCommands.Connect(DateTime.Now), cancellationToken).ConfigureAwait(false);

        var completed = await Task
            .WhenAny(_systemInfoWaiter.Task, Task.Delay(_timeout, cancellationToken))
            .ConfigureAwait(false);

        if (completed != _systemInfoWaiter.Task)
        {
            throw new TimeoutException($"联机超时：{_timeout.TotalSeconds:0.#}s 内未收到 A0 系统配置");
        }

        StartHeartbeat();
    }

    public async Task DisconnectAsync()
    {
        await StopHeartbeatAsync().ConfigureAwait(false);
        Unsubscribe();
        await _channel.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 心跳周期（CBH 会话保活）。下位机 **60 秒**收不到任何下行帧就回收会话（2026-10-11 真机实测，
    /// 表现为 A2 流整段停掉、机器一并停住），这里取 2 秒、留足余量。测试可改小。
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(2);

    public double SpeedMillimetersPerMinute { get; private set; } = 50.0;

    /// <summary>
    /// 手动点动（工作台上升 / 下降按钮）用的速率，mm/min。
    /// 默认 50mm/min = 电机上限（见 <see cref="Gr3MotorSpeed.MaxMillimetersPerMinute"/>）。
    /// **不照搬现役的点动倍率**：现役点动 = 试验速率 × 10（弯曲 50mm/min 参数 → 500mm/min），
    /// 超过 50mm/min 的硬上限会让伺服宕机，所以这里固定在上限内。
    /// </summary>
    public double JogSpeedMillimetersPerMinute { get; set; } = 50.0;

    public Task SetSpeedAsync(double millimetersPerMinute, CancellationToken cancellationToken = default)
    {
        SpeedMillimetersPerMinute = millimetersPerMinute;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 点动工作台（C4H）：方向字 1=正转上升 / 2=反转下降，速率取 <see cref="JogSpeedMillimetersPerMinute"/>。
    /// 执行页的"工作台上升 / 下降"按钮走这里。
    /// </summary>
    public Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default) =>
        JogAtSpeedAsync(direction, JogSpeedMillimetersPerMinute, cancellationToken);

    /// <summary>
    /// 点动，显式指定速率（mm/min）。标定 / 需要精确速率时用，不加点动倍率。
    /// </summary>
    public Task JogAtSpeedAsync(
        JogDirection direction,
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) =>
        SendAndWaitAckAsync(
            Gr3DownlinkCommands.MotorLoad(
                direction == JogDirection.Up
                    ? Gr3Constants.MotorOrderForwardUp
                    : Gr3Constants.MotorOrderReverseDown,
                Gr3MotorSpeed.ToWire(millimetersPerMinute)
            ),
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
                    displaceChannel: (byte)(_displaceChannelIndex + 1)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        await SendAndWaitAckAsync(
                Gr3DownlinkCommands.MotorLoad(
                    Gr3Constants.MotorOrderForwardUp,
                    Gr3MotorSpeed.ToWire(SpeedMillimetersPerMinute)
                ),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>停止电机（C4 方向字 0）。</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        SendAndWaitAckAsync(
            Gr3DownlinkCommands.MotorLoad(Gr3Constants.MotorOrderStop, 0),
            cancellationToken
        );

    /// <summary>复位 / 清零（C3H，用户 2026-10-10 澄清：复位发 C3，不是 C5）。</summary>
    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        SendAndWaitAckAsync(Gr3DownlinkCommands.Zero(0x80, 0xFF), cancellationToken);

    /// <summary>
    /// CBH 心跳（**会话保活**）。协议把 CB 叫"心跳保活"：PC 长时间不发任何下行帧时，
    /// 下位机会回收会话 —— 2026-10-11 真机实测约 **60 秒**后 A2 流直接停掉（机器也一并停住），
    /// 而重新发 C0 联机立刻恢复。所以运行期间必须周期性发本指令。
    /// </summary>
    public Task HeartbeatAsync(CancellationToken cancellationToken = default) =>
        SendAndWaitAckAsync(Gr3DownlinkCommands.Heartbeat(), cancellationToken);

    /// <summary>联机成功后启动心跳循环（幂等）。</summary>
    private void StartHeartbeat()
    {
        if (_heartbeatLoop is not null)
        {
            return;
        }

        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;

        _heartbeatLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(HeartbeatInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    // 只发不等应答：应答通道（_ackWaiter）是"一条命令一个应答"，心跳不能去抢它
                    await SendAsync(Gr3DownlinkCommands.Heartbeat(), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // 断开 / 释放时的正常停机
            }
            catch (Exception)
            {
                // 心跳是尽力而为：发不出去也不打断业务，链路真断了会先由数据流表现出来
            }
        });
    }

    private async Task StopHeartbeatAsync()
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
        await StopHeartbeatAsync().ConfigureAwait(false);
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
            throw new TimeoutException($"等待 A1 应答超时（命令 0x{command.CommandCode:X2}）");
        }

        var status = await _ackWaiter.Task.ConfigureAwait(false);
        if (status != Gr3AckStatus.Success)
        {
            throw new InvalidOperationException($"设备拒绝命令 0x{command.CommandCode:X2}：{status}");
        }
    }
}
