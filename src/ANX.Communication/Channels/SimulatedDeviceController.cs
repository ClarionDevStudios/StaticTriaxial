namespace ANX.Communication.Channels;

using ANX.Core;
using ANX.Core.Control;

/// <summary>
/// 模拟设备：按 <see cref="ITestDeviceController"/> 的接口产生一段"加载 → 峰值 → 破坏 → 回落"的采样，
/// 让控制引擎与曲线在没有真机时也能跑通整条链路。
/// 只在 Debug 构建里编译（Release 用 Compile Remove 排除），交付产物不含模拟器。
/// </summary>
public sealed class SimulatedDeviceController : ITestDeviceController, IAsyncDisposable
{
    private readonly double _peakLoadKN;
    private readonly double _deflectionAtPeakMM;
    private readonly double _sampleIntervalSeconds;
    private readonly double _timeScale;
    private readonly double _contactGapMM;
    private readonly double _loadNoiseRatio;
    private readonly Random _noise = new(20261015); // 固定种子：噪声每次一样，便于复现
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private JogDirection? _jogDirection;
    private double _jogDisplacementMM;
    private double _travelMM;
    private double _displacementOffsetMM;
    private double _lastRawDisplacementMM;
    private double? _returnSpeedMillimetersPerMinute;

    public SimulatedDeviceController(
        double peakLoadKN = 1.5,
        double deflectionAtPeakMM = 0.7,
        double sampleIntervalSeconds = 0.02,
        double timeScale = 1.0,
        double contactGapMM = SampleCurve.DefaultContactGapMM,
        double loadNoiseRatio = 0.005
    )
    {
        _peakLoadKN = peakLoadKN;
        _deflectionAtPeakMM = deflectionAtPeakMM;
        _sampleIntervalSeconds = sampleIntervalSeconds;
        _timeScale = timeScale;
        _contactGapMM = contactGapMM;
        _loadNoiseRatio = loadNoiseRatio;
    }

    public string DeviceName => "模拟器";

    public bool IsConnected { get; private set; }

    /// <summary>与现场真机同值（25.000mm）：模拟器也要逼出"闸门不能超传感器量程"的行为。</summary>
    public double? DisplacementRangeMillimeters => 25.0;

    /// <summary>与现场真机同值（100.000kN）：荷载闸门按"量程 × 系数"算。</summary>
    public double? LoadRangeKN => 100.0;

    /// <summary>模拟器里只要采样循环在跑就算电机在转。</summary>
    public bool? IsMotorRunning => IsConnected && _loop is { IsCompleted: false };

    /// <summary>试验已进行秒数；由控制循环推进（模拟器的采样自带时间戳，仅供产生数据用）。</summary>
    public double ElapsedSeconds { get; set; }

    public double SpeedMillimetersPerMinute { get; private set; } = 50.0;

    /// <summary>平台升降速率（点动用）—— 与试验速率分开，界面按住上下按钮时用这个。</summary>
    public double JogSpeedMillimetersPerMinute { get; set; } = 50.0;

    /// <summary>模拟器只有一个虚拟通道，通道号仅作为契约占位。</summary>
    public int PressChannelNumber { get; set; } = 1;

    /// <summary>模拟器只有一个虚拟通道，位移来源仅作为契约占位。</summary>
    public DisplacementSource DisplacementSource { get; set; } = DisplacementSource.Displace2;

    public event EventHandler<TestSample>? SampleReceived;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        IsConnected = false;
        await StopAsync().ConfigureAwait(false);
    }

    public Task SetSpeedAsync(double millimetersPerMinute, CancellationToken cancellationToken = default)
    {
        SpeedMillimetersPerMinute = millimetersPerMinute;
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        // 一件试验从"当前这台设备的位置"起算：上一件跑完平台已归位，新试件重新放好，
        // 所以行程与偏移都要清零 —— 否则第二件会接着上一件的行程继续走
        //（曲线一开头就在过峰后的尾巴上，判定拟合不出上升段。2026-10-15 踩到）
        _travelMM = 0;
        _displacementOffsetMM = 0;

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 点动工作台：按当前速度移动、荷载近似为 0（空载）。
    /// 复用同一套采样循环与停止逻辑，所以界面"按住上升 / 松开停止"在模拟器上也看得到位移在动。
    /// </summary>
    public Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default)
    {
        _jogDirection = direction;
        return StartAsync(cancellationToken);
    }

    /// <summary>
    /// 归位：反转下降，速率取调用方给的**试验速率**（真机也是这个口径）。
    /// 复用点动那套采样循环，只是位移速度换成归位速率。
    /// </summary>
    public Task ReturnToStartAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    )
    {
        _returnSpeedMillimetersPerMinute = millimetersPerMinute;
        return JogAsync(JogDirection.Down, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _jogDirection = null;
        _returnSpeedMillimetersPerMinute = null;

        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 停止过程中的异常无需上报
            }
        }
    }

    /// <summary>
    /// 复位 = **位移清零**（与真机 C3H「显示数据清零」同义）：把模拟的工作台位置归零，
    /// 但不停采样 —— 接触成立那一刻上位机就会调它，停了就没法继续加载了。
    /// </summary>
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _jogDisplacementMM = 0;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 接触调零（上位机软件调零）：把当前位移记为偏移量，之后净值为 0 起算；不动设备零点。
    /// </summary>
    public Task ZeroDisplacementAsync(CancellationToken cancellationToken = default)
    {
        _displacementOffsetMM = _lastRawDisplacementMM;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }

    private async Task RunAsync(CancellationToken token)
    {
        var seconds = 0.0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var sample =
                    _jogDirection is { } direction
                        ? JogSample(direction, seconds)
                        : LoadSample(_sampleIntervalSeconds * _timeScale, seconds);

                _lastRawDisplacementMM = sample.Displacement;

                // **仿真时钟也要走**：位移按"速率 × 仿真时间"前进，样本的时间戳就必须是同一个时钟，
                // 否则上位机（阶段机）用墙钟计时、曲线用仿真时间走位移，两边的"10 秒"不是一个长度 ——
                // 用户 2026-10-15 实测：timeScale = 5 时"破坏后继续加载 10s"在真机是 0.33mm（2mm/min），
                // 模拟数据却记成 1.67mm，曲线右端拖得极长（"模拟曲线右端不要延长那么多"）。
                ElapsedSeconds += _sampleIntervalSeconds * _timeScale;

                SampleReceived?.Invoke(
                    this,
                    sample with
                    {
                        // 与真机控制器同口径：接触调零是上位机侧偏移（净值 = 原始 − 偏移），不动设备零点
                        Displacement = sample.Displacement - _displacementOffsetMM,
                        RawDisplacement = sample.Displacement,
                    }
                );
                // timeScale 让测试可以加速：每等 _sampleIntervalSeconds，仿真时间前进 _sampleIntervalSeconds × timeScale
                seconds += _sampleIntervalSeconds * _timeScale;
                await Task.Delay(TimeSpan.FromSeconds(_sampleIntervalSeconds), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止
        }
    }

    /// <summary>
    /// 试验采样：平台按**当前设定速率**往前走 dt 秒，荷载按试件响应算
    /// （挠度 = 平台行程 − 接触空行程，接触那一刻上位机会把净值调零）。
    ///
    /// 速率由阶段机切（接近段 10mm/min → 试验段 50mm/min），所以行程必须**增量积分** ——
    /// 用"速率 × 总时长"的话，切速率那一刻行程会跳。
    /// </summary>
    private TestSample LoadSample(double dt, double seconds)
    {
        _travelMM += SpeedMillimetersPerMinute / 60.0 * dt;

        var deflection = _travelMM - _contactGapMM;
        var load = SampleCurve.LoadAt(deflection, _peakLoadKN, _deflectionAtPeakMM);

        // 小噪声：真实传感器读数不是干净的（实测真机相邻差中位 0.007~0.06kN）
        if (_loadNoiseRatio > 0)
        {
            var amplitude = Math.Max(_peakLoadKN * _loadNoiseRatio, 0.002);
            load = Math.Max(0, load + ((_noise.NextDouble() - 0.5) * 2 * amplitude));
        }

        // 位移按真机口径给到 0.001mm
        return new TestSample(load, Math.Round(_travelMM, 3), seconds);
    }

    /// <summary>点动采样：上升位移增、下降位移减（不越过 0），空载荷载≈0。</summary>
    private TestSample JogSample(JogDirection direction, double seconds)
    {
        var travelMM =
            (_returnSpeedMillimetersPerMinute ?? JogSpeedMillimetersPerMinute)
            / 60.0
            * _sampleIntervalSeconds
            * _timeScale;

        _jogDisplacementMM += direction == JogDirection.Up ? travelMM : -travelMM;
        _jogDisplacementMM = Math.Max(0, _jogDisplacementMM);

        return new TestSample(0.01, _jogDisplacementMM, seconds);
    }
}
