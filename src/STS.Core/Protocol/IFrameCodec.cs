namespace STS.Core.Protocol;

/// <summary>
/// 机型帧编解码器：一个机型一个实现，不做公共帧壳（存量机型协议无法统一）。
/// 实现必须是无状态或线程安全的 —— 它会被通道接收线程与指令应答路径并发访问。
/// </summary>
public interface IFrameCodec
{
    /// <summary>机型名（用于界面下拉与日志，例如 "GR-3"）。</summary>
    string CodecName { get; }

    /// <summary>该机型实时数据帧的命令字。模拟器必须由机型自己声明，不能写死。</summary>
    byte TelemetryCommandCode { get; }

    /// <summary>该机型应答帧的命令字。</summary>
    byte AckCommandCode { get; }

    /// <summary>应答帧里表示"成功"的状态值。</summary>
    byte AckOkStatus { get; }

    /// <summary>
    /// 应答是否靠请求序号关联。一问一答的机型（如 GLT-50）为 false，带序号的机型为 true。
    /// 指令层的匹配策略据此选择，不能假设所有机型都能靠序号关联。
    /// </summary>
    bool UsesSequenceCorrelation { get; }

    /// <summary>把指令编成一个完整帧。</summary>
    byte[] Encode(ICommand command);

    /// <summary>
    /// 从缓冲区头部尝试解出一个帧。
    /// 返回 true 时 <paramref name="consumed"/> 为该帧占用的字节数；返回 false 时 <paramref name="consumed"/> 为可安全丢弃的字节数（用于跳过噪声）。
    /// </summary>
    bool TryDecode(ReadOnlySpan<byte> buffer, out DecodedFrame frame, out int consumed);
}
