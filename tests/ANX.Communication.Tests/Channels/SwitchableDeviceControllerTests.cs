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

    /// <summary>
    /// 网口链路监视器**跟着数据来源启停**（用户 2026-10-16）：真机才盯网线、模拟数据停着。
    /// 以前是"模拟器模式不注册这个服务"，于是启动在模拟数据、运行期切回真机就永远没有插拔检测。
    /// </summary>
    [Fact]
    public async Task 链路监视器跟着数据来源启停()
    {
        var source = AppDataSources.Simulator;
        var monitor = new FakeLinkMonitor();

        await using var switchable = new SwitchableDeviceController(
            _ => Task.FromResult(source),
            () => new FakeDevice("真机"),
            () => new FakeDevice("模拟器"),
            monitor
        );

        // 启动在"模拟数据"：连一下（解析来源）→ 监视器必须是停的
        await switchable.ConnectAsync();
        Assert.True(switchable.IsSimulating);
        Assert.False(monitor.Running);

        // 设置里切到"真机试验" → 下次操作解析来源时把监视器起起来
        source = AppDataSources.Device;
        await switchable.ConnectAsync();
        Assert.False(switchable.IsSimulating);
        Assert.True(monitor.Running);

        // 再切回"模拟数据" → 监视器停掉（假设备不参与，报真网线只会误导）
        source = AppDataSources.Simulator;
        await switchable.ConnectAsync();
        Assert.False(monitor.Running);
    }

    /// <summary>只记"开着还是停着"的假监视器（幂等语义与真的一致）。</summary>
    private sealed class FakeLinkMonitor : INetworkLinkMonitor
    {
        public bool Running { get; private set; }

        public NetworkLinkState State { get; } =
            new(NetworkLinkKind.Unknown, string.Empty, "未检测");

        public event EventHandler<NetworkLinkState>? StateChanged;

        public void Start() => Running = true;

        public void Stop() => Running = false;

        public void Check() => StateChanged?.Invoke(this, State);
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
