namespace ANX.Communication.NetworkLink;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ANX.Core.Control;

/// <summary>
/// <see cref="INetworkLinkMonitor"/> 的默认实现：**路由探测定位网卡** + 定时轮询 + 系统网络变更事件。
///
/// 定位网卡的做法（2026-10-14 实测选出来的）：
/// <list type="number">
///   <item>拿一个 UDP 探针 <c>Connect(设备IP)</c>，问系统"去这个地址会走哪个本地地址"
///         —— 这一步不发包，只是让路由表给出答案（现场实测：设备 192.168.0.122 → 本地 192.168.0.100 → 网卡"以太网"）；</item>
///   <item>按本地地址找到那块网卡，**记住它的 Id**，以后按 Id 认它（地址可能因 DHCP 变化）；</item>
///   <item>探针失败（网线已拔、还没有地址）时，退回"地址段匹配"：找 IPv4 与设备同网段的网卡（含 Down 的，
///         Windows 在网线拔掉后还会保留一段时间的地址）。</item>
/// </list>
///
/// **为什么不按"只有一块以太网卡就认它"回落**：2026-10-14 本机枚举发现 WFP/QoS 等过滤驱动
/// （"以太网-QoS Packet Scheduler-0000"、"以太网-WFP …"）也报 <c>NetworkInterfaceType.Ethernet</c> 且 Up，
/// 这么回落会认到假网卡上。
/// </summary>
public sealed class NetworkLinkMonitor : INetworkLinkMonitor, IDisposable
{
    /// <summary>默认轮询间隔：插拔后最多等这么久就报出来（另有系统事件做即时触发）。</summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    private readonly string _remoteHost;
    private readonly TimeSpan _pollInterval;
    private readonly Func<NetworkLinkState>? _probe;
    private readonly object _gate = new();

    private NetworkLinkState _state = new(NetworkLinkKind.Unknown, string.Empty, "尚未检测");
    private string? _adapterId;
    private string _adapterName = string.Empty;
    private Timer? _timer;
    private bool _started;
    private bool _disposed;

    /// <summary>是否已经出过第一次结论。初始的"尚未检测"不算结论 —— 第一次真检测必须报出来。</summary>
    private bool _stateKnown;

    /// <param name="remoteHost">控制器 IP（用它反推走哪块网卡）。</param>
    /// <param name="pollInterval">轮询间隔，默认 <see cref="DefaultPollInterval"/>。</param>
    /// <param name="probe">**仅供测试**：替换"怎么判断链路"，默认走真实网卡。</param>
    public NetworkLinkMonitor(
        string remoteHost,
        TimeSpan? pollInterval = null,
        Func<NetworkLinkState>? probe = null
    )
    {
        _remoteHost = remoteHost;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _probe = probe;
    }

    /// <inheritdoc />
    public NetworkLinkState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<NetworkLinkState>? StateChanged;

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        // 系统事件只是"提前知道"的加速通道：Windows 的网卡事件偶尔会合并/丢，所以**轮询是主判据**
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;

        Check();
        _timer = new Timer(_ => Check(), null, _pollInterval, _pollInterval);
    }

    /// <inheritdoc />
    public void Stop()
    {
        Timer? timer;
        lock (_gate)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            timer = _timer;
            _timer = null;
        }

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        timer?.Dispose();
    }

    /// <inheritdoc />
    public void Check()
    {
        NetworkLinkState next;
        try
        {
            next = _probe?.Invoke() ?? Evaluate();
        }
        catch (Exception ex)
        {
            // 读网卡配置本身失败（少见的驱动/权限问题）：按"未知"报，别把监视循环打死
            next = new NetworkLinkState(NetworkLinkKind.Unknown, _adapterName, $"读取网卡状态失败：{ex.Message}");
        }

        EventHandler<NetworkLinkState>? handler;
        lock (_gate)
        {
            // **只按"链路类别 + 网卡"判变化**，不按 Detail 判：
            // Detail 里带协商速率（实测 WLAN 在 156 / 173.3 Mbps 之间来回跳），
            // 拿整条记录比会把速率抖动当成"链路变了"，于是日志刷屏、操控信息被灌满。
            var unchanged =
                _stateKnown
                && next.Kind == _state.Kind
                && string.Equals(next.AdapterName, _state.AdapterName, StringComparison.Ordinal);

            _state = next; // 结论总是更新（界面读 State 拿到的是最新的 Detail）
            _stateKnown = true;

            if (unchanged)
            {
                return;
            }

            handler = StateChanged;
        }

        // 事件在锁外抛：订阅者（界面、日志）里再调回来不会死锁
        handler?.Invoke(this, next);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();

        lock (_gate)
        {
            _disposed = true;
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => Check();

    /// <summary>真实检测：定位网卡 → 读它的链路状态。</summary>
    private NetworkLinkState Evaluate()
    {
        var adapter = ResolveAdapter();
        if (adapter is null)
        {
            return new NetworkLinkState(
                NetworkLinkKind.AdapterMissing,
                _adapterName,
                $"没找到能到达 {_remoteHost} 的网口（网卡被拔掉？地址段变了？）"
            );
        }

        return adapter.OperationalStatus switch
        {
            OperationalStatus.Up => new NetworkLinkState(
                NetworkLinkKind.Up,
                adapter.Name,
                $"链路正常（{FormatSpeed(adapter.Speed)}）"
            ),
            OperationalStatus.Down => new NetworkLinkState(
                NetworkLinkKind.MediaDisconnected,
                adapter.Name,
                "网口 Down：网线未插 / 对端（交换机、控制器）掉电 / 网卡被禁用"
            ),
            OperationalStatus.Dormant => new NetworkLinkState(
                NetworkLinkKind.MediaDisconnected,
                adapter.Name,
                "网口 Dormant：没有载波"
            ),
            OperationalStatus.NotPresent => new NetworkLinkState(
                NetworkLinkKind.AdapterMissing,
                adapter.Name,
                "网卡不存在"
            ),
            var other => new NetworkLinkState(
                NetworkLinkKind.MediaDisconnected,
                adapter.Name,
                $"网口状态 {other}"
            ),
        };
    }

    /// <summary>找到承载设备流量的网卡（顺序：记住的那块 → 路由探针 → 同网段）。</summary>
    private NetworkInterface? ResolveAdapter()
    {
        var adapters = NetworkInterface.GetAllNetworkInterfaces();

        // ① 上次认准的那块（地址会因 DHCP 变，所以认 Id 不认地址）
        if (_adapterId is { Length: > 0 } remembered)
        {
            var found = adapters.FirstOrDefault(a =>
                string.Equals(a.Id, remembered, StringComparison.OrdinalIgnoreCase)
            );

            if (found is not null)
            {
                return found;
            }
        }

        if (!IPAddress.TryParse(_remoteHost, out var remote) || remote.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        // ② 地址段匹配：设备就在"直连"的网段上，所以**同网段那块网卡才算数**。
        //    放在路由探针前面有两个理由：① 网线刚拔时网卡还留着旧地址，照样认得出来（这是最要紧的一步）；
        //    ② 设备地址若压根不在任何网段（打错 IP），不会被"默认路由那块网卡"冒充成正常链路
        //    （2026-10-14 实测踩过：设备写成 26.26.26.2 时，路由探针回的是走默认路由的 WLAN，报到 WLAN 上了）。
        if (FindBySameSubnet(adapters, remote) is { } bySubnet)
        {
            Remember(bySubnet);
            return bySubnet;
        }

        // ③ 路由探针（不发包，只问路由表"去设备走哪个本地地址"）：给"设备在别的网段/经由路由器"的接法兜底
        var local = TryResolveLocalAddress(remote);
        if (local is not null && FindByAddress(adapters, local) is { } byAddress)
        {
            Remember(byAddress);
            return byAddress;
        }

        return null;
    }

    private void Remember(NetworkInterface adapter)
    {
        lock (_gate)
        {
            _adapterId = adapter.Id;
            _adapterName = adapter.Name;
        }
    }

    private static IPAddress? TryResolveLocalAddress(IPAddress remote)
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(new IPEndPoint(remote, 9));

            return probe.LocalEndPoint is IPEndPoint endpoint
                && !endpoint.Address.Equals(IPAddress.Any)
                // 没有路由时系统会回环回 0.0.0.0 / 回环地址，那不是"真的走这块网卡"
                && !IPAddress.IsLoopback(endpoint.Address)
                ? endpoint.Address
                : null;
        }
        catch (SocketException)
        {
            return null; // 现在没有去设备的路径
        }
    }

    private static NetworkInterface? FindByAddress(
        NetworkInterface[] adapters,
        IPAddress address
    ) =>
        adapters.FirstOrDefault(adapter =>
            adapter
                .GetIPProperties()
                .UnicastAddresses.Any(unicast =>
                    unicast.Address.Equals(address)
                )
        );

    /// <summary>同网段查找：按每块网卡的 IPv4 掩码判断设备地址是否落在它的网段里。</summary>
    private static NetworkInterface? FindBySameSubnet(
        NetworkInterface[] adapters,
        IPAddress remote
    )
    {
        foreach (var adapter in adapters)
        {
            if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
            {
                if (
                    unicast.Address.AddressFamily != AddressFamily.InterNetwork
                    || unicast.IPv4Mask is not { } mask
                    || mask.Equals(IPAddress.Any)
                )
                {
                    continue;
                }

                if (SameSubnet(unicast.Address, remote, mask))
                {
                    return adapter;
                }
            }
        }

        return null;
    }

    private static bool SameSubnet(IPAddress left, IPAddress right, IPAddress mask)
    {
        var a = left.GetAddressBytes();
        var b = right.GetAddressBytes();
        var m = mask.GetAddressBytes();

        if (a.Length != 4 || b.Length != 4 || m.Length != 4)
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            if ((a[i] & m[i]) != (b[i] & m[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatSpeed(long bitsPerSecond) =>
        bitsPerSecond >= 1_000_000_000
            ? $"{bitsPerSecond / 1_000_000_000.0:0.#} Gbps"
            : $"{bitsPerSecond / 1_000_000.0:0.#} Mbps";
}
