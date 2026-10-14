namespace ANX.Core.Tests;

using ANX.Core.Control;

/// <summary>
/// 停机闭环确认（用户 2026-10-14）：发停止 ≠ 停住 —— 只认状态回帧里的电机位。
/// UDP 无连接，实测链路断开 / 无路由时发送照样"成功"，所以判据不能是"发送返回值"。
/// </summary>
public sealed class MotorStopConfirmationTests
{
    [Fact]
    public async Task 状态位翻过来才算确认()
    {
        var device = new FakeDevice { IsMotorRunning = true };

        // 60ms 后"伺服放行"，电机位翻 0
        _ = Task.Run(async () =>
        {
            await Task.Delay(60);
            device.IsMotorRunning = false;
        });

        var stopped = await MotorStopConfirmation.WaitStoppedAsync(
            device,
            TimeSpan.FromSeconds(2)
        );

        Assert.True(stopped);
    }

    [Fact]
    public async Task 本来就没转立即确认()
    {
        var device = new FakeDevice { IsMotorRunning = false };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stopped = await MotorStopConfirmation.WaitStoppedAsync(
            device,
            TimeSpan.FromSeconds(2)
        );
        watch.Stop();

        Assert.True(stopped);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"不该等（实际 {watch.ElapsedMilliseconds}ms）");
    }

    [Fact]
    public async Task 电机位一直是运转则未确认()
    {
        var device = new FakeDevice { IsMotorRunning = true };

        var stopped = await MotorStopConfirmation.WaitStoppedAsync(
            device,
            TimeSpan.FromMilliseconds(200)
        );

        Assert.False(stopped);
    }

    [Fact]
    public async Task 没有回帧_等于未联机_也算未确认()
    {
        // IsMotorRunning = null 就是"设备不上报 / 没联机"：不能当成"停住了"
        var device = new FakeDevice { IsMotorRunning = null };

        var stopped = await MotorStopConfirmation.WaitStoppedAsync(
            device,
            TimeSpan.FromMilliseconds(150)
        );

        Assert.False(stopped);
    }

    /// <summary>只为测这个判据的最小设备替身：除电机位外一律不实现。</summary>
    private sealed class FakeDevice : ITestDeviceController
    {
        public bool? IsMotorRunning { get; set; }

        public string DeviceName => "fake";

        public bool IsConnected => IsMotorRunning is not null;

        public double? DisplacementRangeMillimeters => null;

        public double? LoadRangeKN => null;

        public double ElapsedSeconds { get; set; }

        public double JogSpeedMillimetersPerMinute { get; set; }

        public int PressChannelNumber { get; set; }

        public DisplacementSource DisplacementSource { get; set; }

        public event EventHandler<TestSample>? SampleReceived;

        public Task ConnectAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DisconnectAsync() => throw new NotSupportedException();

        public Task SetSpeedAsync(double millimetersPerMinute, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ReturnToStartAsync(double millimetersPerMinute, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResetAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ZeroDisplacementAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>引用一下事件，免得被"从未使用"的警告烦到（事件本身用不上）。</summary>
        public void Touch() => SampleReceived?.Invoke(this, new TestSample());
    }
}
