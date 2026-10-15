namespace ANX.Communication.Channels;

using ANX.Core;
using ANX.Core.Control;
using ANX.Core.Settings;

/// <summary>
/// **数据来源可切换**的设备外壳（用户 2026-10-15）：系统设置里选"真机试验 / 模拟数据"，
/// 这里按当前设置把调用转发给**真机控制器**或**模拟器**，切换后不需要重启软件。
///
/// 做法：每次操作（联机 / 启动 / 点动 / 读属性…）之前先解析一次"当前该用哪个来源"，
/// 变了就把旧来源停掉、断开、释放，再装配新的 —— 旧来源里那些累积状态（行程、偏移）
/// 不会带到新来源上（§75 那个"模拟器状态跨轮不清"的坑同源）。
///
/// 模拟器只在 Debug 里存在：Release 构建下若设置里选了"模拟数据"，会**回落到真机**
/// （<see cref="IsSimulating"/> 为 false），不会静默假装在出数据。
/// </summary>
public sealed class SwitchableDeviceController : ITestDeviceController, IDeviceSourceSwitch, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<string>> _sourceKey;
    private readonly Func<ITestDeviceController> _deviceFactory;
    private readonly Func<ITestDeviceController>? _simulatorFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ITestDeviceController? _current;
    private string? _currentKey;

    public SwitchableDeviceController(
        Func<CancellationToken, Task<string>> sourceKey,
        Func<ITestDeviceController> deviceFactory,
        Func<ITestDeviceController>? simulatorFactory = null
    )
    {
        _sourceKey = sourceKey;
        _deviceFactory = deviceFactory;
        _simulatorFactory = simulatorFactory;
    }

    /// <inheritdoc />
    public event EventHandler<TestSample>? SampleReceived;

    /// <inheritdoc />
    public bool IsSimulating => string.Equals(_currentKey, AppDataSources.Simulator, StringComparison.Ordinal);

    /// <inheritdoc />
    public string DeviceName => _current?.DeviceName ?? "（尚未选择数据来源）";

    /// <inheritdoc />
    public bool IsConnected => _current?.IsConnected ?? false;

    /// <inheritdoc />
    public double? DisplacementRangeMillimeters => _current?.DisplacementRangeMillimeters;

    /// <inheritdoc />
    public double? LoadRangeKN => _current?.LoadRangeKN;

    /// <inheritdoc />
    public bool? IsMotorRunning => _current?.IsMotorRunning;

    /// <inheritdoc />
    public double ElapsedSeconds
    {
        get => _current?.ElapsedSeconds ?? 0;
        set
        {
            if (_current is not null)
            {
                _current.ElapsedSeconds = value;
            }
        }
    }

    /// <inheritdoc />
    public double JogSpeedMillimetersPerMinute
    {
        get => _current?.JogSpeedMillimetersPerMinute ?? 0;
        set
        {
            if (_current is not null)
            {
                _current.JogSpeedMillimetersPerMinute = value;
            }
        }
    }

    /// <inheritdoc />
    public int PressChannelNumber
    {
        get => _current?.PressChannelNumber ?? 1;
        set
        {
            if (_current is not null)
            {
                _current.PressChannelNumber = value;
            }
        }
    }

    /// <inheritdoc />
    public DisplacementSource DisplacementSource
    {
        get => _current?.DisplacementSource ?? DisplacementSource.Displace2;
        set
        {
            if (_current is not null)
            {
                _current.DisplacementSource = value;
            }
        }
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .ConnectAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task DisconnectAsync() =>
        await (await CurrentAsync(CancellationToken.None).ConfigureAwait(false))
            .DisconnectAsync()
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SetSpeedAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .SetSpeedAsync(millimetersPerMinute, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .StartAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task JogAsync(
        JogDirection direction,
        CancellationToken cancellationToken = default
    ) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .JogAsync(direction, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ReturnToStartAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .ReturnToStartAsync(millimetersPerMinute, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .StopAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ResetAsync(CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .ResetAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ZeroDisplacementAsync(CancellationToken cancellationToken = default) =>
        await (await CurrentAsync(cancellationToken).ConfigureAwait(false))
            .ZeroDisplacementAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task ApplyAsync(CancellationToken cancellationToken = default)
    {
        var wasConnected = IsConnected;
        var target = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var active = await EnsureAsync(target.Key, target.Factory, cancellationToken)
            .ConfigureAwait(false);

        // 旧来源已经在 EnsureAsync 里停掉并断开了；这里按"切换前是否联机"决定新来源要不要联机
        if (wasConnected && !active.IsConnected)
        {
            await active.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReleaseAsync(_current).ConfigureAwait(false);
            _current = null;
            _currentKey = null;
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }

    private async Task<ITestDeviceController> CurrentAsync(CancellationToken cancellationToken)
    {
        var target = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        return await EnsureAsync(target.Key, target.Factory, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>解析当前该用哪个来源（模拟器拿不到时回落真机 —— Release 构建里没有模拟器）。</summary>
    private async Task<(string Key, Func<ITestDeviceController> Factory)> ResolveAsync(
        CancellationToken cancellationToken
    )
    {
        var key = await _sourceKey(cancellationToken).ConfigureAwait(false);
        var wantsSimulator = string.Equals(
            key,
            AppDataSources.Simulator,
            StringComparison.Ordinal
        );

        return wantsSimulator && _simulatorFactory is not null
            ? (AppDataSources.Simulator, _simulatorFactory)
            : (AppDataSources.Device, _deviceFactory);
    }

    private async Task<ITestDeviceController> EnsureAsync(
        string key,
        Func<ITestDeviceController> factory,
        CancellationToken cancellationToken
    )
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current is not null && string.Equals(_currentKey, key, StringComparison.Ordinal))
            {
                return _current;
            }

            await ReleaseAsync(_current).ConfigureAwait(false);

            _current = factory();
            _currentKey = key;
            _current.SampleReceived += OnInnerSample;
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>停掉 + 断开 + 释放一个旧来源；旧来源没联机过就只是个空壳，停一下也无害。</summary>
    private async Task ReleaseAsync(ITestDeviceController? inner)
    {
        if (inner is null)
        {
            return;
        }

        inner.SampleReceived -= OnInnerSample;

        try
        {
            if (inner.IsConnected)
            {
                await inner.StopAsync().ConfigureAwait(false);
                await inner.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // 切换来源时旧来源的收尾失败不该挡住新来源（例如真机已经拔线）
        }

        if (inner is IAsyncDisposable disposable)
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void OnInnerSample(object? sender, TestSample sample) => SampleReceived?.Invoke(this, sample);
}
