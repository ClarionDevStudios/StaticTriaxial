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

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
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

    public Task ResetAsync(CancellationToken cancellationToken = default) => StopAsync();

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }

    /// <summary>
    /// 纯函数形式的采样曲线（便于测试与回放）：6 秒线性加载到峰值，之后 2 秒内回落 35%
    /// —— 用来触发阶段机的破坏判定（荷载下降超过阈值并连续确认）。
    /// </summary>
    public static TestSample SampleAt(double seconds, double peakLoadKN, double displacementAtPeakMM)
    {
        var displacement = displacementAtPeakMM * seconds / 6.0;
        var load = seconds <= 6.0
            ? peakLoadKN * seconds / 6.0
            : peakLoadKN - peakLoadKN * 0.35 * (seconds - 6.0) / 2.0;

        return new TestSample(load, displacement, seconds);
    }

    private async Task RunAsync(CancellationToken token)
    {
        var seconds = 0.0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                SampleReceived?.Invoke(this, SampleAt(seconds, _peakLoadKN, _displacementAtPeakMM));
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
}
