namespace ANX.Protocol.Gr3;

using System.Buffers.Binary;
using ANX.Core.Protocol;

/// <summary>
/// GR-3 两层帧编解码。
///
/// 外层（UDP 数据包）：帧头(3)=0xEE×3 + 数据包长度(1) + 包编号(2) + 指令包 + 校验和(2)。
///   数据包长度 = 除帧头与长度字节之外的全部字节数（= 包编号 + 指令包 + 校验和）。
///   校验和 = 除校验和自身外所有字节的无符号累加取低 16 位。
/// 内层（指令包）：同步符(3)=0xFA 0xFB 0xFC + 帧长度(1) + 命令类型(1) + 数据 + CRC-8(1)。
///   帧长度 = 除同步符与长度字节之外的全部字节数（= 命令类型 + 数据 + CRC）。
///   CRC-8 覆盖范围 = 帧长度字节到数据末尾（即"帧长度"所指的那一段）。
/// 多字节字段一律大端。
///
/// 实现是无状态的吗？不是 —— 它持有一个包编号计数器（每发一帧 +1，协议要求）。
/// 计数器用 <see cref="Interlocked"/> 递增，因此实例本身是线程安全的。
/// </summary>
public sealed class Gr3FrameCodec : IFrameCodec
{
    private int _sequence;

    public string CodecName => "GR-3";

    public byte TelemetryCommandCode => Gr3Constants.UplinkRealtimeData;

    public byte AckCommandCode => Gr3Constants.UplinkAck;

    public byte AckOkStatus => Gr3Constants.AckSuccess;

    /// <summary>GR-3 的应答带包编号，可靠包编号关联请求与应答（与 GLT-50 的一问一答不同）。</summary>
    public bool UsesSequenceCorrelation => true;

    /// <summary>取下一个包编号（0x0001 起，回绕）。</summary>
    public ushort NextSequence() => (ushort)(Interlocked.Increment(ref _sequence) & 0xFFFF);

    /// <summary>把包编号计数器归零（重新联机时用；测试也用它取得确定性输出）。</summary>
    public void ResetSequence() => Interlocked.Exchange(ref _sequence, 0);

    /// <summary>按 <see cref="IFrameCodec"/> 编码：自动取下一个包编号。</summary>
    public byte[] Encode(ICommand command) =>
        BuildDatagram(command.CommandCode, command.BuildPayload(), NextSequence());

    /// <summary>从一段字节流里解出下一个帧；见 <see cref="TryDecode"/> 语义说明。</summary>
    public bool TryDecode(ReadOnlySpan<byte> buffer, out DecodedFrame frame, out int consumed) =>
        TryDecodeFrame(buffer, out frame, out consumed);

    /// <summary>
    /// 构造一个完整的 UDP 数据帧。包编号由调用方给定，便于测试取得确定性字节。
    /// </summary>
    public static byte[] BuildDatagram(byte commandCode, ReadOnlySpan<byte> payload, ushort sequence)
    {
        var instructionLength = 1 + payload.Length + 1; // 命令类型 + 数据 + CRC-8
        if (instructionLength > Gr3Constants.MaxInstructionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(payload),
                $"指令包长度 {instructionLength} 超过协议上限 0x{Gr3Constants.MaxInstructionLength:X2}"
            );
        }

        var packetLength =
            Gr3Constants.UdpSequenceLength
            + Gr3Constants.InstructionHeaderLength
            + instructionLength
            + Gr3Constants.UdpChecksumLength;

        var frame = new byte[Gr3Constants.UdpHeaderLength + packetLength];
        var pos = 0;

        // 外层帧头 + 长度
        frame[pos++] = 0xEE;
        frame[pos++] = 0xEE;
        frame[pos++] = 0xEE;
        frame[pos++] = (byte)packetLength;

        // 包编号（大端）
        frame[pos++] = (byte)(sequence >> 8);
        frame[pos++] = (byte)sequence;

        // 内层同步符 + 长度 + 命令类型
        frame[pos++] = Gr3Constants.InstructionSync0;
        frame[pos++] = Gr3Constants.InstructionSync1;
        frame[pos++] = Gr3Constants.InstructionSync2;
        frame[pos++] = (byte)instructionLength;
        frame[pos++] = commandCode;

        payload.CopyTo(frame.AsSpan(pos));
        pos += payload.Length;

        // CRC-8：从"指令包帧长度"字节起，覆盖 instructionLength 个字节
        var crcStart = Gr3Constants.UdpHeaderLength + Gr3Constants.UdpSequenceLength + 3;
        frame[pos++] = Gr3Crc8.Compute(frame, crcStart, instructionLength);

        // UDP 校验和：除自身外全部字节无符号累加，取低 16 位（大端）
        var sum = 0;
        for (var i = 0; i < pos; i++)
        {
            sum += frame[i];
        }

        frame[pos++] = (byte)((sum >> 8) & 0xFF);
        frame[pos] = (byte)(sum & 0xFF);

        return frame;
    }

    /// <summary>
    /// 从字节流头部解出一个完整帧（外层 + 内层）。
    /// <paramref name="consumed"/> 的语义：
    /// 0 = 帧不完整，等待更多数据（不要丢弃）；&gt;0 且返回 false = 跳掉这么多字节重新同步；
    /// 返回 true = 一帧解出，consumed 为该帧占用的字节数。
    /// </summary>
    public static bool TryDecodeFrame(
        ReadOnlySpan<byte> buffer,
        out DecodedFrame frame,
        out int consumed
    )
    {
        frame = default;
        consumed = 0;

        if (buffer.IsEmpty)
        {
            return false;
        }

        var header = -1;
        for (var i = 0; i + Gr3Constants.UdpSyncLength <= buffer.Length; i++)
        {
            if (buffer[i] == 0xEE && buffer[i + 1] == 0xEE && buffer[i + 2] == 0xEE)
            {
                header = i;
                break;
            }
        }

        if (header < 0)
        {
            // 没找到同步头；保留最后 2 字节（可能是被截断的 0xEE 0xEE）
            consumed = Math.Max(0, buffer.Length - (Gr3Constants.UdpSyncLength - 1));
            return false;
        }

        if (header > 0)
        {
            consumed = header;
            return false;
        }

        if (buffer.Length < Gr3Constants.UdpHeaderLength)
        {
            return false;
        }

        var packetLength = buffer[3];
        var totalLength = Gr3Constants.UdpHeaderLength + packetLength;
        var minimumLength =
            Gr3Constants.UdpHeaderLength
            + Gr3Constants.UdpSequenceLength
            + Gr3Constants.InstructionHeaderLength
            + 2
            + Gr3Constants.UdpChecksumLength;

        if (totalLength < minimumLength)
        {
            consumed = 1;
            return false;
        }

        if (buffer.Length < totalLength)
        {
            return false; // 尾部不完整，等更多数据
        }

        // 外层校验和
        var checksumOffset = totalLength - Gr3Constants.UdpChecksumLength;
        var givenChecksum = BinaryPrimitives.ReadUInt16BigEndian(
            buffer.Slice(checksumOffset, Gr3Constants.UdpChecksumLength)
        );
        var calculated = 0;
        for (var i = 0; i < checksumOffset; i++)
        {
            calculated += buffer[i];
        }

        if ((ushort)calculated != givenChecksum)
        {
            consumed = 1;
            return false;
        }

        // 内层指令包
        var instructionStart = Gr3Constants.UdpHeaderLength + Gr3Constants.UdpSequenceLength;
        var instruction = buffer.Slice(instructionStart, checksumOffset - instructionStart);

        if (!TryParseInstruction(instruction, out frame, out var instructionConsumed)
            || instructionConsumed != instruction.Length)
        {
            consumed = totalLength; // 外层对、内层坏：整帧丢弃
            return false;
        }

        consumed = totalLength;
        return true;
    }

    /// <summary>
    /// 解析一个指令包（不含 UDP 外层）。
    /// 成功的帧：<c>Status</c> 取 A1H 应答的第一个数据字节（状态），<c>Payload</c> 为其余数据；
    /// 其它命令字的 <c>Status</c> 为 0、<c>Payload</c> 为整个数据域。
    /// </summary>
    public static bool TryParseInstruction(
        ReadOnlySpan<byte> buffer,
        out DecodedFrame frame,
        out int consumed
    )
    {
        frame = default;
        consumed = 0;

        if (buffer.Length < Gr3Constants.InstructionHeaderLength + 2)
        {
            return false;
        }

        if (
            buffer[0] != Gr3Constants.InstructionSync0
            || buffer[1] != Gr3Constants.InstructionSync1
            || buffer[2] != Gr3Constants.InstructionSync2
        )
        {
            consumed = 1;
            return false;
        }

        var instructionLength = buffer[3];
        if (instructionLength < 2) // 至少要有命令类型 + CRC
        {
            consumed = 1;
            return false;
        }

        var totalLength = Gr3Constants.InstructionHeaderLength + instructionLength;
        if (buffer.Length < totalLength)
        {
            return false;
        }

        var givenCrc = buffer[totalLength - 1];
        var calculatedCrc = Gr3Crc8.Compute(buffer, Gr3Constants.InstructionHeaderLength - 1, instructionLength);
        if (calculatedCrc != givenCrc)
        {
            consumed = 1;
            return false;
        }

        var commandCode = buffer[4];
        var data = buffer.Slice(5, instructionLength - 2);

        byte status = 0;
        if (commandCode == Gr3Constants.UplinkAck && data.Length >= 1)
        {
            status = data[0];
            data = data[1..];
        }

        frame = new DecodedFrame(commandCode, status, data.ToArray());
        consumed = totalLength;
        return true;
    }
}
