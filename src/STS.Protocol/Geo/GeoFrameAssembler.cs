namespace STS.Protocol.Geo;

/// <summary>
/// 串口字节流 → 帧：把任意切分的字节块喂进来，解出完整帧；
/// 垃圾字节 / 坏帧按"跳 1 字节重同步"处理（与现役拆包的容错口径一致原则）。
/// 非线程安全（由通道接收回调的单一路径调用）。
/// </summary>
public sealed class GeoFrameAssembler
{
    private const int MaxBufferBytes = 8192;

    private byte[] _buffer = new byte[512];
    private int _length;

    /// <summary>因重同步跳过的字节数（诊断用；含坏帧丢弃与噪声）。</summary>
    public long ResyncByteCount { get; private set; }

    /// <summary>喂一段字节，返回本次解出的全部帧（可能为空）。</summary>
    public IReadOnlyList<GeoFrame> Feed(ReadOnlySpan<byte> chunk)
    {
        EnsureCapacity(_length + chunk.Length);
        chunk.CopyTo(_buffer.AsSpan(_length));
        _length += chunk.Length;

        var frames = new List<GeoFrame>();
        var offset = 0;

        while (offset < _length)
        {
            var status = GeoFrameCodec.TryDecode(
                _buffer.AsSpan(offset, _length - offset),
                out var frame,
                out var consumed
            );

            if (status == GeoDecodeStatus.NeedMoreData)
            {
                break;
            }

            if (status == GeoDecodeStatus.Ok)
            {
                frames.Add(frame!);
            }
            else
            {
                ResyncByteCount++;
            }

            offset += consumed;
        }

        if (offset > 0)
        {
            _length -= offset;

            if (_length > 0)
            {
                _buffer.AsSpan(offset, _length).CopyTo(_buffer);
            }
        }

        // 防御：持续噪声别把缓冲撑爆（丢掉整段并记账）
        if (_length >= MaxBufferBytes)
        {
            ResyncByteCount += _length;
            _length = 0;
        }

        return frames;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed <= _buffer.Length)
        {
            return;
        }

        var size = _buffer.Length;

        while (size < needed)
        {
            size *= 2;
        }

        Array.Resize(ref _buffer, size);
    }
}
