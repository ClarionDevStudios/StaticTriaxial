namespace ANX.Communication.Channels;

using System.Net;
using System.Net.Sockets;
using ANX.Core.Communication;

/// <summary>
/// UDP 通道。只负责收发**数据报**，不解析任何帧结构 —— 拆帧是协议层的事。
/// 收到的每个数据报原样交给订阅者（GR-3 会把多条帧打包进同一个数据报，因此不做长度裁剪）。
/// </summary>
public sealed class UdpChannel(UdpChannelOptions options) : ICommChannel
{
    private readonly object _gate = new();
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    public ChannelKind Kind => ChannelKind.Udp;

    public string DisplayName => $"UDP {options.RemoteHost}:{options.RemotePort}";

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _socket is not null;
            }
        }
    }

    public event EventHandler<ReceivedChunk>? DataReceived;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_socket is not null)
            {
                return Task.CompletedTask;
            }

            if (!IPAddress.TryParse(options.RemoteHost, out var remoteAddress))
            {
                throw new ArgumentException($"UDP 远端地址无效：{options.RemoteHost}", nameof(options));
            }

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, options.LocalPort));
            socket.Connect(new IPEndPoint(remoteAddress, options.RemotePort));

            _socket = socket;
            _cts = new CancellationTokenSource();
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(socket, _cts.Token), CancellationToken.None);
        }

        return Task.CompletedTask;
    }

    public async Task CloseAsync()
    {
        Socket? socket;
        CancellationTokenSource? cts;
        Task? loop;

        lock (_gate)
        {
            socket = _socket;
            cts = _cts;
            loop = _receiveLoop;

            _socket = null;
            _cts = null;
            _receiveLoop = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        socket?.Dispose();

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 关闭过程中的接收异常无需上报。
            }
        }

        cts?.Dispose();
    }

    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default
    )
    {
        Socket socket;
        lock (_gate)
        {
            socket = _socket ?? throw new InvalidOperationException("UDP 通道尚未打开");
        }

        await socket.SendAsync(data, SocketFlags.None, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private async Task ReceiveLoopAsync(Socket socket, CancellationToken token)
    {
        var buffer = new byte[8192];

        try
        {
            while (!token.IsCancellationRequested)
            {
                var received = await socket
                    .ReceiveAsync(buffer, SocketFlags.None, token)
                    .ConfigureAwait(false);

                if (received <= 0)
                {
                    continue;
                }

                var chunk = new byte[received];
                buffer.AsSpan(0, received).CopyTo(chunk);

                Publish(new ReceivedChunk(chunk));
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
        catch (SocketException)
        {
            // socket 已被关闭
        }
        catch (ObjectDisposedException)
        {
            // 正常关闭
        }
    }

    /// <summary>
    /// 逐个订阅者隔离异常：一个订阅者抛异常不能打死整个数据发布循环
    /// （否则表现为"再也收不到数据"，极难排查）。
    /// </summary>
    private void Publish(ReceivedChunk chunk)
    {
        var handlers = DataReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<ReceivedChunk>)handler)(this, chunk);
            }
            catch (Exception)
            {
                // 订阅者的问题由订阅者自己负责上报，这里只保证发布循环继续。
            }
        }
    }
}
