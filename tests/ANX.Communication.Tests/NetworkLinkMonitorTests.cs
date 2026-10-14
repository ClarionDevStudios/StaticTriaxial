namespace ANX.Communication.Tests;

using ANX.Communication.NetworkLink;
using ANX.Core.Control;

/// <summary>
/// 网口链路监视（用户 2026-10-14）：事件只在**状态变化**时抛、探针出错不打死轮询、Stop 真停。
///
/// 真实网卡的读取靠实机核对（拔网线 / 换地址段），这里测的是"状态机与节流"这一层 ——
/// 用可注入的探针把网卡换掉，跑得快也能覆盖边界。
/// </summary>
public sealed class NetworkLinkMonitorTests
{
    private static NetworkLinkState Up(string adapter = "以太网") =>
        new(NetworkLinkKind.Up, adapter, "链路正常（1 Gbps）");

    private static NetworkLinkState Down(string adapter = "以太网") =>
        new(NetworkLinkKind.MediaDisconnected, adapter, "网口 Down：网线未插");

    [Fact]
    public void 状态不变不抛事件_变了才抛并且带对状态()
    {
        var current = Up();
        using var monitor = new NetworkLinkMonitor(
            "192.168.0.122",
            TimeSpan.FromMilliseconds(20),
            () => current
        );

        var changes = new List<NetworkLinkState>();
        monitor.StateChanged += (_, state) => changes.Add(state);

        monitor.Start();
        Assert.Single(changes); // 启动先测一次，把初始状态报出来
        Assert.True(changes[^1].IsUp);
        Assert.True(monitor.State.IsUp);

        current = Down(); // 拔网线
        monitor.Check();
        Assert.Equal(2, changes.Count);
        Assert.False(changes[^1].IsUp);
        Assert.Equal(NetworkLinkKind.MediaDisconnected, changes[^1].Kind);
        Assert.Equal("以太网", changes[^1].AdapterName);

        monitor.Check(); // 还是断的：不该再抛（否则每秒刷屏）
        monitor.Check();
        Assert.Equal(2, changes.Count);

        current = Up(); // 插回去
        monitor.Check();
        Assert.Equal(3, changes.Count);
        Assert.True(changes[^1].IsUp);
    }

    [Fact]
    public void 只有速率抖动的详情变化时不抛事件()
    {
        // 实测：WLAN 的协商速率会在 156 / 173.3 Mbps 之间来回跳 —— 那不是"链路变了"
        var current = new NetworkLinkState(NetworkLinkKind.Up, "WLAN", "链路正常（156 Mbps）");
        using var monitor = new NetworkLinkMonitor(
            "192.168.0.122",
            TimeSpan.FromMilliseconds(20),
            () => current
        );

        var changes = new List<NetworkLinkState>();
        monitor.StateChanged += (_, state) => changes.Add(state);

        monitor.Start();
        Assert.Single(changes);

        current = new NetworkLinkState(NetworkLinkKind.Up, "WLAN", "链路正常（173.3 Mbps）");
        monitor.Check();
        monitor.Check();
        Assert.Single(changes); // 只更新内部结论，不抛事件
        Assert.Equal("链路正常（173.3 Mbps）", monitor.State.Detail); // State 拿到的是最新的

        // 换网卡（例如从 WLAN 跳到以太网）算真变化
        current = new NetworkLinkState(NetworkLinkKind.Up, "以太网", "链路正常（10 Mbps）");
        monitor.Check();
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void 探针抛异常时按未知报_监视循环不死()
    {
        var shouldThrow = true;
        using var monitor = new NetworkLinkMonitor(
            "192.168.0.122",
            TimeSpan.FromMilliseconds(20),
            () => shouldThrow ? throw new InvalidOperationException("网卡驱动抽风") : Up()
        );

        var changes = new List<NetworkLinkState>();
        monitor.StateChanged += (_, state) => changes.Add(state);

        monitor.Start();
        Assert.Single(changes);
        Assert.Equal(NetworkLinkKind.Unknown, changes[^1].Kind);
        Assert.Contains("网卡驱动抽风", changes[^1].Detail, StringComparison.Ordinal);

        // 恢复正常：下一轮要能报回来（说明循环没被打死）
        shouldThrow = false;
        monitor.Check();
        Assert.Equal(2, changes.Count);
        Assert.True(changes[^1].IsUp);
    }

    [Fact]
    public async Task Stop之后不再轮询()
    {
        var probes = 0;
        using var monitor = new NetworkLinkMonitor(
            "192.168.0.122",
            TimeSpan.FromMilliseconds(20),
            () =>
            {
                Interlocked.Increment(ref probes);
                return Up();
            }
        );

        monitor.Start();
        await Task.Delay(150);
        Assert.True(Volatile.Read(ref probes) >= 2, $"轮询没跑起来（探测次数 {probes}）");

        monitor.Stop();
        var afterStop = Volatile.Read(ref probes);
        await Task.Delay(150);

        Assert.Equal(afterStop, Volatile.Read(ref probes));
    }

    [Fact]
    public void Start与Stop可以重复调()
    {
        using var monitor = new NetworkLinkMonitor(
            "192.168.0.122",
            TimeSpan.FromMilliseconds(20),
            () => Up()
        );

        monitor.Start();
        monitor.Start(); // 幂等
        monitor.Stop();
        monitor.Stop(); // 幂等

        Assert.True(monitor.State.IsUp);
    }
}
