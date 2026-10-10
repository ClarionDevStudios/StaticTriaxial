namespace ANX.Core.Communication;

/// <summary>通道收到的一段原始字节。拆帧由协议层负责，通道层不做任何解析。</summary>
public readonly record struct ReceivedChunk(ReadOnlyMemory<byte> Data);

/// <summary>
/// 通讯通道抽象。通道层只管"收发字节"，不认识任何命令与帧结构。
/// </summary>
public interface ICommChannel : IAsyncDisposable
{
    /// <summary>通道种类。</summary>
    ChannelKind Kind { get; }

    /// <summary>给人看的通道名（用于界面与日志）。</summary>
    string DisplayName { get; }

    /// <summary>是否已打开。</summary>
    bool IsOpen { get; }

    /// <summary>收到数据。实现必须保证回调在后台线程串行触发。</summary>
    event EventHandler<ReceivedChunk>? DataReceived;

    /// <summary>打开通道。</summary>
    Task OpenAsync(CancellationToken cancellationToken = default);

    /// <summary>关闭通道（可重复调用）。</summary>
    Task CloseAsync();

    /// <summary>发送一段字节。</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
