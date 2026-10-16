namespace STS.Protocol.Geo;

using STS.Core;
using STS.Core.Communication;
using STS.Core.Control;

/// <summary>
/// 土工三轴仪（GR-1）设备控制器：把 V1.7 协议接成框架的 <see cref="ITestDeviceController"/>。
///
/// - 联机：`C0('O',1)` 等 `A0`（读量程 / 小数位）→ `C0('O',2)` 进入主动回送；
/// - 启动：**先 `C3('T',1)` 再 `C1`**（用户 2026-10-16 定）；停止：`C1(停)` → `C3('T',0)`；
/// - 换向：**先停后转**（在 <see cref="SendMotorAsync"/> 单一出口里做，调用方不必记得）；
/// - 围压（C2）：**施加 = 单发一条指令、等应答即完**——稳压由下位机完成（用户 2026-10-16）；
///   卸除 = 现役同帧（目标全 0 + 停止位）；参数默认照现役调用口径（见三个属性）；
/// - 换算（与现役逐条对齐，`uDataProcessingController.pas` / `GetAxialDeformation`）：
///   `值 = raw / 10^Digits`（压力 kN、位移 mm）；净位移 = 原始 − 软件偏移（接触调零不动设备）；
///   现役 `AxialPressure = kN ÷ 面积(cm²) ×10⁴ = kPa` —— 与判定器 σ = P(N)/A(mm²)×10³ 同源。
///   **荷载调零 / 位移调零都是上位机软件零点**（存偏移量，不发设备指令；GR-1 没有清零指令）。
/// - 命令应答为 `A2`（1=成功）；超时 / 被拒不静默：抛异常由上层给提示。
/// </summary>
public sealed class GeoDeviceController : ITestDeviceController, IConfiningPressureControl, IAsyncDisposable
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultReversalPause = TimeSpan.FromMilliseconds(500);

    private readonly ICommChannel _channel;
    private readonly TimeSpan _commandTimeout;
    private readonly TimeSpan _reversalPause;
    private readonly GeoFrameAssembler _assembler = new();
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private readonly object _stateGate = new();

    private TaskCompletionSource<GeoFrame>? _pending;
    private byte _pendingCommand;
    private GeoSystemConfig? _config;
    private byte _motorOrder; // 0 停 / 1 上 / 2 下
    private double _testSpeedMillimetersPerMinute = 4.59;
    private double _latestRawDisplacementMM;
    private double _latestRawLoadKN;
    private double _zeroOffsetMM;
    private double _zeroLoadOffsetKN;
    private byte _latestMainStatus;
    private bool _hasSample;

    public GeoDeviceController(
        ICommChannel channel,
        TimeSpan? commandTimeout = null,
        TimeSpan? reversalPause = null
    )
    {
        ArgumentNullException.ThrowIfNull(channel);

        _channel = channel;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
        _reversalPause = reversalPause ?? DefaultReversalPause;
        _channel.DataReceived += OnChannelData;
    }

    public string DeviceName => $"土工三轴仪（{_channel.DisplayName}）";

    public bool IsConnected { get; private set; }

    public double? DisplacementRangeMillimeters =>
        _config is null
            ? null
            : _config.Displacement1Scale / Math.Pow(10, _config.Displacement1Digits);

    public double? LoadRangeKN =>
        _config is null
            ? null
            : PressScaleRaw / Math.Pow(10, PressDigits);

    public bool? IsMotorRunning
    {
        get
        {
            lock (_stateGate)
            {
                return _hasSample ? (_latestMainStatus & (0x08 | 0x10)) != 0 : null;
            }
        }
    }

    public double ElapsedSeconds { get; set; }

    public event EventHandler<TestSample>? SampleReceived;

    public double JogSpeedMillimetersPerMinute { get; set; } = 2.4; // 现役点动 2400（0.001mm/min）

    public int PressChannelNumber { get; set; } = 1;

    public DisplacementSource DisplacementSource { get; set; } = DisplacementSource.Displace2;

    /// <summary>
    /// 围压运行速度（C2 的 `WYSPEED`，单位 0.1mm/min，协议窗 1~500）。
    /// 默认照现役调用口径 400 → 40.0mm/min（`土工三轴软件通讯重构版\uMainForm.pas:991`）。
    /// </summary>
    public double ConfiningPressureSpeedMillimetersPerMinute { get; set; } = 40.0;

    /// <summary>
    /// 稳压速度（C2 的 `CONTSPEED`，单位 0.1mm/min，协议窗 1~100）。
    /// 默认照现役调用口径 50 → 5.0mm/min。
    /// </summary>
    public double StableSpeedMillimetersPerMinute { get; set; } = 5.0;

    /// <summary>
    /// 加载杆面积（C2 的 `Loading rod area` 字段；单位 cm²、带三位小数 → 原始值 = cm² × 1000）。
    /// 现役是**设置项**（INI `config\LoadingRodArea`，0 = 未设置）；默认取现役黄金向量口径
    /// 19.635cm² → 19635。**真机对一次**：按现场实际加载杆填入。
    /// </summary>
    public double LoadingRodAreaSquareCentimeters { get; set; } = 19.635;

    private int PressScaleRaw => PressChannelNumber == 2 ? _config!.Press2Scale : _config!.Press1Scale;

    private int PressDigits => PressChannelNumber == 2 ? _config!.Press2Digits : _config!.Press1Digits;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        await _channel.OpenAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // C0('O',1)：下位机回一次 A0 后进入联机模式
            var configFrame = await SendCommandAsync(
                    GeoCommands.Connect(order: 1),
                    GeoCommands.SystemConfigCommand,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (!GeoSystemConfig.TryParse(configFrame, out var config) || config is null)
            {
                throw new InvalidOperationException("A0 系统配置解析失败");
            }

            _config = config;

            // C0('O',2)：进入主动回送 A1
            var ackFrame = await SendCommandAsync(
                    GeoCommands.Connect(order: 2),
                    GeoCommands.AckCommand,
                    cancellationToken
                )
                .ConfigureAwait(false);

            EnsureAckSuccess(ackFrame, "C0 联机（主动回送）");

            IsConnected = true;
        }
        catch
        {
            await _channel.CloseAsync().ConfigureAwait(false);
            _config = null;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        await _channel.CloseAsync().ConfigureAwait(false);

        lock (_stateGate)
        {
            _config = null;
            _motorOrder = 0;
            _hasSample = false;
            _zeroOffsetMM = 0;
            _zeroLoadOffsetKN = 0;
            _latestRawDisplacementMM = 0;
            _latestRawLoadKN = 0;
        }
    }

    public async Task SetSpeedAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    )
    {
        _testSpeedMillimetersPerMinute = millimetersPerMinute;

        // 电机正在运转且速度有变化时**重发 C1**（协议规定同向运转时只有再发指令才改速度）
        var order = _motorOrder;

        if (order != 0)
        {
            await SendMotorAsync(order, millimetersPerMinute, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        RequireConnected();

        // 启动序（用户 2026-10-16 定）：**先 C3 后 C1**
        await SendTestStateAsync(action: 1, cancellationToken).ConfigureAwait(false);
        await SendMotorAsync(order: 1, _testSpeedMillimetersPerMinute, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        await SendMotorAsync(order: 0, speedMillimetersPerMinute: 0, cancellationToken)
            .ConfigureAwait(false);
        await SendTestStateAsync(action: 0, cancellationToken).ConfigureAwait(false);
    }

    public Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default)
    {
        var order = direction == JogDirection.Up ? (byte)1 : (byte)2;

        return SendMotorAsync(order, JogSpeedMillimetersPerMinute, cancellationToken);
    }

    public Task ReturnToStartAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) => SendMotorAsync(order: 2, millimetersPerMinute, cancellationToken);

    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "GR-1 协议没有设备复位指令；位移清零请走 ZeroDisplacementAsync（上位机软件调零）"
        );

    public Task ZeroDisplacementAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            // 接触清零 = 上位机软件调零（存偏移量），不动设备零点（用户 2026-10-12 既定口径）
            _zeroOffsetMM = _hasSample ? _latestRawDisplacementMM : 0;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ZeroLoadAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateGate)
        {
            // 荷载调零 = 上位机软件调零（存偏移量），不动设备零点（规范 4.3 的步骤；用户 2026-10-16）
            _zeroLoadOffsetKN = _hasSample ? _latestRawLoadKN : 0;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 施加围压 σ3：**单发一条 C2**、等 `A2` 成功即返回（用户 2026-10-16）——
    /// 稳压由下位机自己完成，这里不轮询、不调节。
    /// 反压不启用（T1236 用不上；现役同帧传 `1/1`，**真机对一次**）。
    /// </summary>
    public async Task ApplyConfiningPressureAsync(
        double targetKPa,
        CancellationToken cancellationToken = default
    )
    {
        if (targetKPa <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetKPa),
                targetKPa,
                "围压目标必须为正（kPa）"
            );
        }

        RequireConnected();

        var frame = GeoCommands.ConfiningPressure(
            confiningTargetKPa: (int)Math.Round(targetKPa),
            backTargetKPa: 0,
            startConfining: true,
            startBack: false,
            confiningSpeedRaw: ToSpeedRaw(ConfiningPressureSpeedMillimetersPerMinute, maxRaw: 500),
            backSpeedRaw: 0,
            stableSpeedRaw: ToSpeedRaw(StableSpeedMillimetersPerMinute, maxRaw: 100),
            loadingRodAreaRaw: ToLoadingRodAreaRaw()
        );

        var ackFrame = await SendCommandAsync(frame, GeoCommands.AckCommand, cancellationToken)
            .ConfigureAwait(false);

        EnsureAckSuccess(ackFrame, "C2 围压指令");
    }

    /// <summary>
    /// 卸除围压 / 反压：现役同帧（目标全 0 + 停止位 + 加载杆面积）。未联机时静默返回。
    /// </summary>
    public async Task ReleaseConfiningPressureAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        var frame = GeoCommands.ConfiningPressure(
            confiningTargetKPa: 0,
            backTargetKPa: 0,
            startConfining: false,
            startBack: false,
            confiningSpeedRaw: 0,
            backSpeedRaw: 0,
            stableSpeedRaw: 0,
            loadingRodAreaRaw: ToLoadingRodAreaRaw()
        );

        var ackFrame = await SendCommandAsync(frame, GeoCommands.AckCommand, cancellationToken)
            .ConfigureAwait(false);

        EnsureAckSuccess(ackFrame, "C2 卸除压力指令");
    }

    public async ValueTask DisposeAsync()
    {
        _channel.DataReceived -= OnChannelData;
        await DisconnectAsync().ConfigureAwait(false);
        _commandGate.Dispose();
    }

    /// <summary>速率换算：raw = 四舍五入（mm/min × 10），截断到协议窗 [1, maxRaw]。</summary>
    private static int ToSpeedRaw(double millimetersPerMinute, int maxRaw) =>
        (int)Math.Round(Math.Clamp(millimetersPerMinute * 10.0, 1, maxRaw));

    /// <summary>加载杆面积换算：raw = 四舍五入（cm² × 1000），负数截 0。</summary>
    private uint ToLoadingRodAreaRaw() =>
        (uint)Math.Round(Math.Clamp(LoadingRodAreaSquareCentimeters, 0.0, uint.MaxValue / 1000.0) * 1000.0);

    private void OnChannelData(object? sender, ReceivedChunk chunk)
    {
        foreach (var frame in _assembler.Feed(chunk.Data.Span))
        {
            switch (frame.Command)
            {
                case GeoCommands.RealtimeCommand:
                    HandleRealtime(frame);
                    break;

                case GeoCommands.SystemConfigCommand:
                case GeoCommands.AckCommand:
                    var pending = _pending;

                    if (pending is not null && frame.Command == _pendingCommand)
                    {
                        _pending = null;
                        pending.TrySetResult(frame);
                    }

                    break;
            }
        }
    }

    private void HandleRealtime(GeoFrame frame)
    {
        if (!GeoRealtimeSample.TryParse(frame, out var sample) || sample is null)
        {
            return;
        }

        var config = _config;

        if (config is null)
        {
            return;
        }

        var loadKN = PressChannelNumber == 2
            ? sample.Press2Raw / Math.Pow(10, config.Press2Digits)
            : sample.Press1Raw / Math.Pow(10, config.Press1Digits);

        var rawDisplacementMM = ResolveDisplacement(sample, config);

        double netDisplacementMM;
        double netLoadKN;

        lock (_stateGate)
        {
            _latestRawDisplacementMM = rawDisplacementMM;
            _latestRawLoadKN = loadKN;
            _latestMainStatus = sample.MainStatus;
            _hasSample = true;
            netDisplacementMM = rawDisplacementMM - _zeroOffsetMM;
            netLoadKN = loadKN - _zeroLoadOffsetKN;
        }

        // 围压（PRESS3，原始值单位 1kPa）+ 稳压位（SATAE Bit10）：
        // 只在 A0 报"围压通道有效"时给值，否则上层显示"—"
        var confiningKPa = config.Press3Valid ? (double?)sample.Press3Raw : null;
        var confiningStable = config.Press3Valid ? (bool?)sample.ConfiningStable : null;

        SampleReceived?.Invoke(
            this,
            new TestSample(netLoadKN, netDisplacementMM, ElapsedSeconds)
            {
                RawDisplacement = rawDisplacementMM,
                ConfiningPressureKPa = confiningKPa,
                ConfiningStable = confiningStable,
            }
        );
    }

    private double ResolveDisplacement(GeoRealtimeSample sample, GeoSystemConfig config)
    {
        var source = DisplacementSource;
        var (first, second) = source.Channels();
        var value = ValueOf(source, first, sample, config);

        if (source.IsAverage() && second != first)
        {
            value = (value + ValueOf(source, second, sample, config)) / 2.0;
        }

        return value;
    }

    private static double ValueOf(
        DisplacementSource source,
        int channel,
        GeoRealtimeSample sample,
        GeoSystemConfig config
    )
    {
        if (source.IsMicrometer())
        {
            // 千分表 3-6：2B，原始值 /1000 = mm（现役同口径）
            return channel switch
            {
                0 => sample.Displacement3Raw / 1000.0,
                1 => sample.Displacement4Raw / 1000.0,
                2 => sample.Displacement5Raw / 1000.0,
                _ => sample.Displacement6Raw / 1000.0,
            };
        }

        return channel switch
        {
            0 => sample.Displacement1Raw / Math.Pow(10, config.Displacement1Digits),
            1 => sample.Displacement2Raw / Math.Pow(10, config.Displacement2Digits),
            // A1 只有两路 4B 位移（通道 3/4 未随帧上传）；越界先回落通道 1（真机对一次再定）
            _ => sample.Displacement1Raw / Math.Pow(10, config.Displacement1Digits),
        };
    }

    private async Task SendMotorAsync(
        byte order,
        double speedMillimetersPerMinute,
        CancellationToken cancellationToken
    )
    {
        if (order != 0)
        {
            RequireConnected();
        }

        var currentOrder = _motorOrder;

        // 换向规矩（现役同口径）：先停 → 等一拍 → 再转
        if (order != 0 && currentOrder != 0 && order != currentOrder)
        {
            await SendCommandAsync(
                    GeoCommands.Motor(GeoCommands.MotorOrder.Stop, 0),
                    GeoCommands.AckCommand,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (_reversalPause > TimeSpan.Zero)
            {
                await Task.Delay(_reversalPause, cancellationToken).ConfigureAwait(false);
            }
        }

        var frame = order == 0
            ? GeoCommands.Motor(GeoCommands.MotorOrder.Stop, 0)
            : GeoCommands.Motor(
                (GeoCommands.MotorOrder)order,
                (int)Math.Round(Math.Clamp(speedMillimetersPerMinute, 0, 65.535) * 1000.0)
            );

        var ackFrame = await SendCommandAsync(frame, GeoCommands.AckCommand, cancellationToken)
            .ConfigureAwait(false);

        EnsureAckSuccess(ackFrame, "C1 电机指令");
        _motorOrder = order;
    }

    private async Task SendTestStateAsync(byte action, CancellationToken cancellationToken)
    {
        var ackFrame = await SendCommandAsync(
                GeoCommands.TestState(action),
                GeoCommands.AckCommand,
                cancellationToken
            )
            .ConfigureAwait(false);

        EnsureAckSuccess(ackFrame, "C3 试验状态");
    }

    private async Task<GeoFrame> SendCommandAsync(
        byte[] frame,
        byte expectedCommand,
        CancellationToken cancellationToken
    )
    {
        await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var completion = new TaskCompletionSource<GeoFrame>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            _pending = completion;
            _pendingCommand = expectedCommand;

            await _channel.SendAsync(frame, cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var completed = await Task.WhenAny(
                    completion.Task,
                    Task.Delay(_commandTimeout, timeout.Token)
                )
                .ConfigureAwait(false);

            if (completed != completion.Task)
            {
                throw new TimeoutException(
                    $"等待 {expectedCommand:X2} 应答超时（{_commandTimeout.TotalMilliseconds}ms）"
                );
            }

            timeout.Cancel();

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending = null;
            _commandGate.Release();
        }
    }

    private static void EnsureAckSuccess(GeoFrame frame, string what)
    {
        if (!GeoAck.TryParse(frame, out var ack) || ack is null)
        {
            throw new InvalidOperationException($"{what}：A2 应答解析失败");
        }

        if (ack.Status != GeoAckStatus.Success)
        {
            throw new InvalidOperationException($"{what}被设备拒绝（A2 = {(byte)ack.Status}）");
        }
    }

    private void RequireConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("尚未联机（先 ConnectAsync）");
        }
    }
}
