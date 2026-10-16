namespace STS.Protocol.Geo;

using STS.Core.Communication;

/// <summary>
/// **模拟下位机回环通道**（Debug-only；Release 由 csproj 排除）：
/// 把 <see cref="GeoSimulator"/> 接成一根"虚拟串口线"——
/// 上层发出去的每一帧原样喂给模拟器，模拟器的应答与 A1 流从 <see cref="DataReceived"/> 回来。
///
/// 用途：让**真实的协议栈**（GeoDeviceController → 编解码 → 采样）在没有真机时也能整链跑通；
/// 时间倍率用于把"10 分钟的加载"压缩到演示长度。
/// </summary>
public sealed class GeoLoopbackChannel : ICommChannel
{
    private readonly GeoSimulator _simulator;
    private readonly TimeSpan _tickInterval;
    private readonly double _timeScale;
    private readonly object _simGate = new();
    private readonly object _raiseGate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public GeoLoopbackChannel(
        GeoSimulator simulator,
        TimeSpan? tickInterval = null,
        double timeScale = 1.0
    )
    {
        ArgumentNullException.ThrowIfNull(simulator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeScale);

        _simulator = simulator;
        _tickInterval = tickInterval ?? TimeSpan.FromMilliseconds(20);
        _timeScale = timeScale;
    }

    public ChannelKind Kind => ChannelKind.Simulator;

    public string DisplayName =>
        _timeScale == 1.0 ? "模拟下位机（V1.7）" : $"模拟下位机（V1.7 ×{_timeScale:0.#}）";

    public bool IsOpen { get; private set; }

    public event EventHandler<ReceivedChunk>? DataReceived;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen)
        {
            return Task.CompletedTask;
        }

        IsOpen = true;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        IsOpen = false;

        var cts = _cts;
        var loop = _loop;
        _cts = null;
        _loop = null;

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 关闭过程中的循环异常无需上报。
            }
        }
    }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default
    )
    {
        byte[] responses;

        lock (_simGate)
        {
            responses = _simulator.Feed(data.Span);
        }

        Raise(responses);

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_tickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                byte[] bytes;

                lock (_simGate)
                {
                    bytes = _simulator.Tick(_tickInterval.TotalSeconds * _timeScale);
                }

                Raise(bytes);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭路径。
        }
    }

    private void Raise(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return;
        }

        lock (_raiseGate)
        {
            DataReceived?.Invoke(this, new ReceivedChunk(bytes));
        }
    }
}
