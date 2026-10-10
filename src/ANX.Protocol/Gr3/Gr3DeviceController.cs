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
/// **待真机确认**：C4 的方向字（'U' 上升 / 'D' 下降 / 'S' 停止）目前按参考实现的用法写，需空载试一次。
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
    private double _elapsedSeconds;

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

    /// <summary>A0H 系统配置（决定原始码值怎么换算成工程量）。</summary>
    public Gr3A0SystemInfo? SystemInfo { get; private set; }

    /// <summary>A4H 设备信息。</summary>
    public Gr3DeviceInfo? DeviceInfo { get; private set; }

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
    }

    public async Task DisconnectAsync()
    {
        Unsubscribe();
        await _channel.CloseAsync().ConfigureAwait(false);
    }

    public double SpeedMillimetersPerMinute { get; private set; } = 50.0;

    public Task SetSpeedAsync(double millimetersPerMinute, CancellationToken cancellationToken = default)
    {
        SpeedMillimetersPerMinute = millimetersPerMinute;
        return Task.CompletedTask;
    }

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
                    (ushort)Math.Round(SpeedMillimetersPerMinute)
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

    public async ValueTask DisposeAsync()
    {
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
