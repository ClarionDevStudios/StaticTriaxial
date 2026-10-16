namespace STS.Core.Protocol;

/// <summary>从字节流里解出的一个帧。</summary>
/// <param name="CommandCode">命令字（机型自定义）。</param>
/// <param name="Status">应答状态（非应答帧为 0）。</param>
/// <param name="Payload">数据域（不含帧头、长度、校验）。</param>
public readonly record struct DecodedFrame(
    byte CommandCode,
    byte Status,
    ReadOnlyMemory<byte> Payload
);
