namespace ANX.Tools.Gr3Probe;

using System.Globalization;
using System.Text;
using ANX.Communication;
using ANX.Communication.Channels;
using ANX.Core.Protocol;
using ANX.Protocol.Gr3;

/// <summary>
/// GR-3 联机探针（只读）。
///
/// **只发**：C0 联机、CB 心跳、CAH 读设备 ID、CAH 读通道配置。
/// **不发**：电机（C4/C5）、清零（C3）、试验控制（C6）、温度 / 围压（C7/C8）—— 这些会动机器。
///
/// 用途：① 验证 ANX 自己写的 GR-3 编解码器能跟真机对上；
/// ② 把设备回送的**原始字节**存成抓帧文件，作为黄金用例与"文档 vs 实现"冲突表的证据。
/// </summary>
internal static class Program
{
    private const string DefaultHost = "192.168.0.122";
    private const int DefaultPort = 5001;

    private static readonly object Gate = new();
    private static readonly List<string> RawLog = [];
    private static readonly List<DecodedFrame> Pending = [];
    private static readonly Dictionary<byte, int> CommandCounts = [];
    private const int MaxRealtimeLogEntries = 40;
    private static int _realtimeLogged;

    private static async Task<int> Main(string[] args)
    {
        var host = GetArg(args, "--host") ?? DefaultHost;
        var port = int.Parse(GetArg(args, "--port") ?? DefaultPort.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var localPort = int.Parse(GetArg(args, "--local-port") ?? DefaultPort.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var waitMs = int.Parse(GetArg(args, "--wait") ?? "1500", CultureInfo.InvariantCulture);
        var channelId = byte.Parse(GetArg(args, "--channel") ?? "1", CultureInfo.InvariantCulture);

        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("=== GR-3 联机探针（只读：联机 / 心跳 / 读设备 ID / 读通道配置）===");
        Console.WriteLine(
            $"远端 {host}:{port}   本地端口 {localPort}   每步等待 {waitMs}ms   读通道配置用通道号 {channelId}（协议规定 1-12）"
        );
        Console.WriteLine();

        var codec = new Gr3FrameCodec();

        await using var channel = new UdpChannel(new UdpChannelOptions(host, port, localPort));
        channel.DataReceived += (_, e) =>
        {
            var datagram = e.Data.ToArray();
            var span = datagram.AsSpan();
            var decoded = new List<DecodedFrame>();

            while (Gr3FrameCodec.TryDecodeFrame(span, out var frame, out var consumed))
            {
                decoded.Add(frame);
                span = span[consumed..];
            }

            lock (Gate)
            {
                RawLog.Add($"← {datagram.Length,4} B  {ToHex(datagram)}");
                foreach (var frame in decoded)
                {
                    CommandCounts[frame.CommandCode] = CommandCounts.GetValueOrDefault(frame.CommandCode) + 1;

                    // A2 是 120Hz 实时流：日志里只留前若干条，其余靠计数体现。
                    if (frame.CommandCode == Gr3Constants.UplinkRealtimeData)
                    {
                        if (_realtimeLogged >= MaxRealtimeLogEntries)
                        {
                            continue;
                        }

                        _realtimeLogged++;
                    }

                    RawLog.Add(
                        $"     解码: 命令 0x{frame.CommandCode:X2}  状态 {frame.Status}  数据 {frame.Payload.Length} B  {ToHex(frame.Payload.Span)}"
                    );
                }

                if (decoded.Count == 0)
                {
                    RawLog.Add("     （解不出完整帧）");
                }

                Pending.AddRange(decoded);
            }

            Report(datagram, decoded);
        };

        try
        {
            await channel.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"打开 UDP 通道失败：{ex.Message}");
            Console.WriteLine(
                localPort == DefaultPort
                    ? $"提示：本地端口 {DefaultPort} 可能被现役沥青软件占用 —— 先关闭现役软件，或改用 --local-port 0。"
                    : "提示：检查 IP / 端口是否正确。"
            );
            return 2;
        }

        var now = DateTime.Now;
        await SendAndCollectAsync(codec, channel, "① C0 联机", Gr3DownlinkCommands.Connect(now), waitMs);
        await SendAndCollectAsync(codec, channel, "② CB 心跳", Gr3DownlinkCommands.Heartbeat(), waitMs);
        await SendAndCollectAsync(codec, channel, "③ CAH 读设备 ID (order=2)", Gr3DownlinkCommands.ReadDeviceId(), waitMs);
        await SendAndCollectAsync(codec, channel, $"④ CAH 读通道配置 (order=1, 通道 {channelId})", Gr3DownlinkCommands.ReadChannelConfig(1, channelId), waitMs);

        await channel.CloseAsync();

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "抓帧");
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var file = Path.Combine(
            outputDirectory,
            $"gr3-{now:yyyyMMdd-HHmmss}.log"
        );

        var header = new StringBuilder();
        header.AppendLine($"# GR-3 联机抓帧（只读探针）");
        header.AppendLine($"# 时间：{now:yyyy-MM-dd HH:mm:ss}");
        header.AppendLine($"# 远端：{host}:{port}   本地端口：{localPort}");
        header.AppendLine($"# 说明：只发了 C0 联机 / CB 心跳 / CAH 读设备 ID / CAH 读通道配置");
        header.AppendLine();

        lock (Gate)
        {
            File.WriteAllText(file, header + string.Join(Environment.NewLine, RawLog), Encoding.UTF8);
        }

        Console.WriteLine();
        Console.WriteLine("=== 本次收到的上行帧统计 ===");
        lock (Gate)
        {
            foreach (var pair in CommandCounts.OrderBy(p => p.Key))
            {
                Console.WriteLine($"    0x{pair.Key:X2}  ×{pair.Value}");
            }

            if (CommandCounts.Count == 0)
            {
                Console.WriteLine("    （一个上行帧都没收到）");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"抓帧已保存：{file}");
        return 0;
    }

    private static async Task SendAndCollectAsync(
        Gr3FrameCodec codec,
        UdpChannel channel,
        string step,
        ICommand command,
        int waitMs
    )
    {
        List<DecodedFrame> received;
        byte[] frame;

        lock (Gate)
        {
            Pending.Clear();
        }

        try
        {
            frame = codec.Encode(command);
            await channel.SendAsync(frame);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{step}：发送失败 —— {ex.Message}");
            return;
        }

        Console.WriteLine($"{step}   发送 {frame.Length} B：{ToHex(frame)}");

        await Task.Delay(waitMs);

        lock (Gate)
        {
            received = [.. Pending];
        }

        if (received.Count == 0)
        {
            Console.WriteLine("     ← 无应答");
            return;
        }

        var summary = received
            .GroupBy(f => f.CommandCode)
            .Select(group =>
            {
                var text = Describe(group.First());
                return group.Count() > 1 ? $"{text} ×{group.Count()}" : text;
            });

        Console.WriteLine($"     ← {string.Join("  |  ", summary)}");
    }

    private static void Report(byte[] datagram, List<DecodedFrame> decoded)
    {
        if (decoded.Count == 0)
        {
            Console.WriteLine($"     ← 收到 {datagram.Length} B，但解不出完整帧");
            return;
        }

        // 每条数据报一行摘要：A2 只报条数（120Hz 刷屏没有意义），其余逐条说明。
        var parts = decoded
            .GroupBy(f => f.CommandCode)
            .Select(group =>
            {
                var first = group.First();
                var summary = Describe(first);
                return group.Count() > 1 ? $"{summary} ×{group.Count()}" : summary;
            });

        Console.WriteLine($"     ← {datagram.Length,4} B   {string.Join("  |  ", parts)}");
    }

    private static string Describe(DecodedFrame frame) =>
        frame.CommandCode switch
        {
            Gr3Constants.UplinkAck =>
                $"A1 应答  状态 {frame.Status}"
                + (
                    Gr3UplinkParser.TryParseAck(frame, out var status)
                        ? $"（{status switch { Gr3AckStatus.Success => "成功", Gr3AckStatus.CannotExecute => "无法执行", _ => "校验失败" }}）"
                        : string.Empty
                ),
            Gr3Constants.UplinkDeviceId when Gr3UplinkParser.TryParseDeviceInfo(frame, out var info) =>
                $"A4 设备信息  ID「{info.Id}」  类型 {info.DeviceType}（{info.DeviceTypeName}）",
            Gr3Constants.UplinkDeviceId =>
                $"A4 设备信息（数据域 {frame.Payload.Length} B）",
            Gr3Constants.UplinkSystemInfo => $"A0 系统配置（{frame.Payload.Length} B）",
            Gr3Constants.UplinkChannelConfig => $"A3 通道配置（{frame.Payload.Length} B）",
            Gr3Constants.UplinkRealtimeData => $"A2 实时数据（{frame.Payload.Length} B）",
            _ => $"未知命令（{frame.Payload.Length} B）",
        };

    private static string ToHex(ReadOnlySpan<byte> bytes) =>
        string.Join(' ', bytes.ToArray().Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
