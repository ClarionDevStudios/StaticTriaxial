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
    private readonly double _displacementAtPeakMM;
    private readonly double _sampleIntervalSeconds;
    private readonly double _timeScale;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private JogDirection? _jogDirection;
    private double _jogDisplacementMM;
    private double _displacementOffsetMM;
    private double _lastRawDisplacementMM;

    public SimulatedDeviceController(
        double peakLoadKN = 9.0,
        double displacementAtPeakMM = 5.0,
        double sampleIntervalSeconds = 0.02,
        double timeScale = 1.0
    )
    {
        _peakLoadKN = peakLoadKN;
        _displacementAtPeakMM = displacementAtPeakMM;
        _sampleIntervalSeconds = sampleIntervalSeconds;
        _timeScale = timeScale;
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

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _jogDirection = null;

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
                        : SampleCurve.At(seconds, _peakLoadKN, _displacementAtPeakMM);

                _lastRawDisplacementMM = sample.Displacement;
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

    /// <summary>点动采样：上升位移增、下降位移减（不越过 0），空载荷载≈0。</summary>
    private TestSample JogSample(JogDirection direction, double seconds)
    {
        var travelMM =
            SpeedMillimetersPerMinute / 60.0 * _sampleIntervalSeconds * _timeScale;

        _jogDisplacementMM += direction == JogDirection.Up ? travelMM : -travelMM;
        _jogDisplacementMM = Math.Max(0, _jogDisplacementMM);

        return new TestSample(0.01, _jogDisplacementMM, seconds);
    }
}
