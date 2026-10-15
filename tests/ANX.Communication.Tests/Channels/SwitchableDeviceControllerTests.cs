namespace ANX.Communication.Tests.Channels;

using ANX.Communication.Channels;
using ANX.Core;
using ANX.Core.Control;
using ANX.Core.Settings;

/// <summary>
/// 系统设置里的"数据来源"开关（用户 2026-10-15）：真机试验 / 模拟数据，选完**立即**生效。
/// 这里盯三件事：默认走真机；切到模拟后调用都转发给模拟器；切来源时旧设备会被停掉断开。
/// </summary>
public sealed class SwitchableDeviceControllerTests
{
    [Fact]
    public async Task 默认走真机_切到模拟后转发给模拟器()
    {
        var source = AppDataSources.Device;
        var device = new FakeDevice("真机");
        var simulator = new FakeDevice("模拟器");

        await using var switchable = new SwitchableDeviceController(
            _ => Task.FromResult(source),
            () => device,
            () => simulator
        );

        await switchable.ConnectAsync();
        Assert.False(switchable.IsSimulating);
        Assert.Equal("真机", switchable.DeviceName);
        Assert.True(device.Connected);
        Assert.False(simulator.Connected);

        // 设置里改成"模拟数据" → ApplyAsync 让新来源立刻生效
        source = AppDataSources.Simulator;
        await switchable.ApplyAsync();

        Assert.True(switchable.IsSimulating);
        Assert.Equal("模拟器", switchable.DeviceName);
        Assert.True(simulator.Connected);
        Assert.False(device.Connected); // 旧来源（真机）被停掉并断开
    }

    [Fact]
    public async Task 没有模拟器时选模拟也回落真机()
    {
        // Release 构建里没有模拟器 → 选了"模拟数据"不能静默假装在出数据
        var source = AppDataSources.Simulator;
        var device = new FakeDevice("真机");

        await using var switchable = new SwitchableDeviceController(
            _ => Task.FromResult(source),
            () => device,
            simulatorFactory: null
        );

        await switchable.ConnectAsync();

        Assert.False(switchable.IsSimulating);
        Assert.Equal("真机", switchable.DeviceName);
        Assert.True(device.Connected);
    }

    [Fact]
    public async Task 采样事件应转发出来_切换后不再收旧来源的()
    {
        var source = AppDataSources.Device;
        var device = new FakeDevice("真机");
        var simulator = new FakeDevice("模拟器");

        await using var switchable = new SwitchableDeviceController(
            _ => Task.FromResult(source),
            () => device,
            () => simulator
        );

        var seen = new List<double>();
        switchable.SampleReceived += (_, sample) => seen.Add(sample.Load);

        await switchable.ConnectAsync();
        device.Raise(1.0);

        source = AppDataSources.Simulator;
        await switchable.ConnectAsync(); // 下一次操作就会切过去
        device.Raise(2.0); // 旧来源：不该再传出来
        simulator.Raise(3.0);

        Assert.Equal([1.0, 3.0], seen);
    }

    private sealed class FakeDevice(string name) : ITestDeviceController
    {
        public string DeviceName { get; } = name;

        public bool Connected { get; private set; }

        public event EventHandler<TestSample>? SampleReceived;

        public void Raise(double loadKN) =>
            SampleReceived?.Invoke(this, new TestSample(loadKN, 0, 0));

        public bool IsConnected => Connected;

        public double? DisplacementRangeMillimeters => 25.0;

        public double? LoadRangeKN => 100.0;

        public bool? IsMotorRunning => false;

        public double ElapsedSeconds { get; set; }

        public double JogSpeedMillimetersPerMinute { get; set; } = 50;

        public int PressChannelNumber { get; set; } = 1;

        public DisplacementSource DisplacementSource { get; set; } = DisplacementSource.Displace2;

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Connected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            Connected = false;
            return Task.CompletedTask;
        }

        public Task SetSpeedAsync(
            double millimetersPerMinute,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task JogAsync(
            JogDirection direction,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task ReturnToStartAsync(
            double millimetersPerMinute,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ZeroDisplacementAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
