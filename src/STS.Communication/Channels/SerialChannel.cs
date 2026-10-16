namespace STS.Communication.Channels;

using STS.Core.Communication;

/// <summary>
/// 串口通道（RS232 / USB 转串口）。只负责收发**字节**，不解析任何帧 —— 拆帧是协议层的事。
/// 对外契约与 <see cref="UdpChannel"/> 同款（<see cref="ICommChannel"/>）；
/// 串口那半的实现借自 IAP 的 `SerialChannel`（同为本机自研代码，移植时按本工程契约重写对外面）。
/// </summary>
public sealed class SerialChannel : ICommChannel
{
    private readonly SerialChannelOptions _options;
    private readonly Func<ISerialPortDevice> _portFactory;
    private readonly object _gate = new();
    private readonly object _raiseGate = new();

    private ISerialPortDevice? _port;

    public SerialChannel(SerialChannelOptions options)
        : this(options, () => new SystemSerialPortDevice(options)) { }

    /// <summary>测试接缝：注入假串口设备（`InternalsVisibleTo("STS.Communication.Tests")`）。</summary>
    internal SerialChannel(SerialChannelOptions options, Func<ISerialPortDevice> portFactory)
    {
        _options = options;
        _portFactory = portFactory;
    }

    public ChannelKind Kind => ChannelKind.Serial;

    public string DisplayName => $"串口 {_options.PortName}@{_options.BaudRate}";

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _port?.IsOpen ?? false;
            }
        }
    }

    public event EventHandler<ReceivedChunk>? DataReceived;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_port is not null)
            {
                return Task.CompletedTask;
            }

            var port = _portFactory();
            port.DataReceived += OnDataReceived;
            port.ErrorRaised += OnErrorRaised;

            try
            {
                port.Open();
            }
            catch
            {
                port.DataReceived -= OnDataReceived;
                port.ErrorRaised -= OnErrorRaised;
                port.Dispose();
                throw;
            }

            _port = port;
        }

        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        ISerialPortDevice? port;

        lock (_gate)
        {
            port = _port;
            _port = null;
        }

        if (port is not null)
        {
            port.DataReceived -= OnDataReceived;
            port.ErrorRaised -= OnErrorRaised;

            try
            {
                port.Close();
            }
            catch (Exception)
            {
                // 关闭阶段的异常无需上报（通道已视为关闭）。
            }

            port.Dispose();
        }

        return Task.CompletedTask;
    }

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default
    )
    {
        ISerialPortDevice? port;

        lock (_gate)
        {
            port = _port;
        }

        if (port is null || !port.IsOpen)
        {
            return ValueTask.CompletedTask;
        }

        var bytes = data.ToArray();

        return new ValueTask(
            Task.Run(
                () =>
                {
                    try
                    {
                        port.Write(bytes, 0, bytes.Length);
                    }
                    catch (Exception)
                    {
                        Fault();
                    }
                },
                cancellationToken
            )
        );
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    private void OnDataReceived()
    {
        byte[] buffer;
        int read;

        try
        {
            lock (_gate)
            {
                var port = _port;

                if (port is null)
                {
                    return;
                }

                var count = port.BytesToRead;

                if (count <= 0)
                {
                    return;
                }

                buffer = new byte[count];
                read = port.Read(buffer, 0, count);
            }
        }
        catch (Exception)
        {
            Fault();
            return;
        }

        if (read > 0)
        {
            // 契约：回调在后台线程**串行**触发。
            lock (_raiseGate)
            {
                DataReceived?.Invoke(this, new ReceivedChunk(buffer.AsMemory(0, read)));
            }
        }
    }

    private void OnErrorRaised(string message) => Fault();

    private void Fault()
    {
        lock (_gate)
        {
            try
            {
                _port?.Close();
            }
            catch (Exception)
            {
                // 出错路径上的关闭失败忽略：通道已置为"未打开"。
            }
        }
    }
}

/// <summary>
/// 串口设备接缝：真实实现包 <see cref="System.IO.Ports.SerialPort"/>，测试注入假设备。
/// 只暴露通道层用得到的最小面。
/// </summary>
internal interface ISerialPortDevice : IDisposable
{
    bool IsOpen { get; }

    int BytesToRead { get; }

    event Action? DataReceived;

    event Action<string>? ErrorRaised;

    void Open();

    void Close();

    int Read(byte[] buffer, int offset, int count);

    void Write(byte[] buffer, int offset, int count);
}

/// <summary>真实串口设备（System.IO.Ports 适配）。</summary>
internal sealed class SystemSerialPortDevice(SerialChannelOptions options) : ISerialPortDevice
{
    private readonly System.IO.Ports.SerialPort _port = new(
        options.PortName,
        options.BaudRate,
        options.Parity,
        options.DataBits,
        options.StopBits
    )
    {
        Handshake = options.Handshake,
        ReadTimeout = 500,
        WriteTimeout = 500,
    };

    public bool IsOpen => _port.IsOpen;

    public int BytesToRead => _port.BytesToRead;

    public event Action? DataReceived;

    public event Action<string>? ErrorRaised;

    public void Open()
    {
        _port.DataReceived += OnDataReceived;
        _port.ErrorReceived += OnErrorReceived;
        _port.Open();
    }

    public void Close()
    {
        if (_port.IsOpen)
        {
            _port.Close();
        }
    }

    public int Read(byte[] buffer, int offset, int count) => _port.Read(buffer, offset, count);

    public void Write(byte[] buffer, int offset, int count) => _port.Write(buffer, offset, count);

    public void Dispose()
    {
        _port.DataReceived -= OnDataReceived;
        _port.ErrorReceived -= OnErrorReceived;
        _port.Dispose();
    }

    private void OnDataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e) =>
        DataReceived?.Invoke();

    private void OnErrorReceived(object sender, System.IO.Ports.SerialErrorReceivedEventArgs e) =>
        ErrorRaised?.Invoke(e.EventType.ToString());
}
