namespace ANX.Tools.Gr3Probe;

using System.Globalization;
using System.Diagnostics;
using System.Text;
using ANX.Core;
using ANX.Communication;
using ANX.Communication.Channels;
using ANX.Core.Protocol;
using ANX.Core.Control;
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

        // 电机空载试（会真的动工作台）—— 必须显式加 --motor-test 才走这条
        if (Array.IndexOf(args, "--motor-test") >= 0)
        {
            return await RunMotorTestAsync(host, port, localPort, waitMs);
        }

        // 速率标定（会真的动工作台）—— 实测「速度字段 1 个单位 = 多少 mm/min」
        if (Array.IndexOf(args, "--speed-calib") >= 0)
        {
            return await RunSpeedCalibrationAsync(host, port, localPort, waitMs);
        }

        // 会话保活观测（只读）：--watch <秒> [--heartbeat <秒>]
        if (GetArg(args, "--watch") is { } watchSeconds)
        {
            return await RunWatchAsync(
                host,
                port,
                localPort,
                int.Parse(watchSeconds, CultureInfo.InvariantCulture),
                double.Parse(GetArg(args, "--heartbeat") ?? "0", CultureInfo.InvariantCulture),
                useCb: string.Equals(GetArg(args, "--keepalive") ?? "cah", "cb", StringComparison.OrdinalIgnoreCase)
            );
        }

        // 运行中改速率（会真的动工作台）：验证"改速度必须重发 C4"这条协议语义
        if (Array.IndexOf(args, "--speed-change") >= 0)
        {
            return await RunSpeedChangeAsync(host, port, localPort, waitMs);
        }

        // 换向（会真的动工作台）：验证"先发停止、再发启动"（用户 2026-10-12 明确要求）
        if (Array.IndexOf(args, "--reverse-test") >= 0)
        {
            return await RunReverseTestAsync(host, port, localPort, waitMs);
        }

        // 逐通道原始值诊断（只读、不动电机）：核对"位移1+2平均值"这类通道口径
        if (GetArg(args, "--dump-channels") is { } dumpSeconds)
        {
            return await RunChannelDumpAsync(
                host,
                port,
                localPort,
                int.Parse(dumpSeconds, CultureInfo.InvariantCulture)
            );
        }

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

    /// <summary>
    /// 电机空载试：联机 → 读状态 → C4 正转 1 秒 → 停止 → C3 清零 → C4 反转 1 秒 → 停止 → C3 清零。
    /// **会真的动工作台**，只在显式传 `--motor-test` 时执行。
    ///
    /// 每步都打印机器状态位 + 当前荷载/位移，这样：
    /// ① C4 方向字 0/1/2 三位都能验证（正转位、反转位）；
    /// ② C3 清零不再是"没超时"了事 —— 正转让位移离开零点后清零，位移应回 ~0。
    /// </summary>
    private static async Task<int> RunMotorTestAsync(string host, int port, int localPort, int waitMs)
    {
        Console.WriteLine("=== 电机空载试（会真的动工作台：请空载、且行程上下都留余量）===");
        Console.WriteLine($"远端 {host}:{port}   本地端口 {localPort}");

        await using var channel = new UdpChannel(new UdpChannelOptions(host, port, localPort));
        await using var controller = new Gr3DeviceController(
            channel,
            pressChannelIndex: 0,
            displacementSource: DisplacementSource.Displace2
        );

        var lastSample = default(TestSample);
        var sampleCount = 0;
        controller.SampleReceived += (_, sample) =>
        {
            lastSample = sample;
            sampleCount++;
        };

        try
        {
            await controller.ConnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"联机失败：{ex.Message}");
            return 2;
        }

        Console.WriteLine("联机成功（A0 已收到）");
        await Task.Delay(Math.Max(800, waitMs));
        ReportStatus("① 联机后", controller, lastSample, sampleCount);

        await controller.JogAsync(JogDirection.Up);
        Console.WriteLine("已发 C4H 正转上升");
        await Task.Delay(1500);
        ReportStatus("② C4 正转中（期望 正转=True）", controller, lastSample, sampleCount);

        await controller.StopAsync();
        Console.WriteLine("已发 C4H 停止");
        await Task.Delay(Math.Max(800, waitMs));
        ReportStatus("③ C4 停止后（期望两位都 False）", controller, lastSample, sampleCount);

        var beforeZero = lastSample.Displacement;

        await controller.ResetAsync();
        Console.WriteLine("已发 C3H 清零");
        await Task.Delay(Math.Max(800, waitMs));
        ReportStatus("④ C3 清零后（期望位移回 ~0）", controller, lastSample, sampleCount);
        Console.WriteLine(
            $"     清零对照：清零前位移 {beforeZero:F4} mm → 清零后 {lastSample.Displacement:F4} mm"
        );

        await controller.JogAsync(JogDirection.Down);
        Console.WriteLine("已发 C4H 反转下降");
        await Task.Delay(1500);
        ReportStatus("⑤ C4 反转中（期望 反转=True）", controller, lastSample, sampleCount);

        await controller.StopAsync();
        Console.WriteLine("已发 C4H 停止");
        await Task.Delay(Math.Max(800, waitMs));
        ReportStatus("⑥ C4 停止后", controller, lastSample, sampleCount);

        await controller.ResetAsync();
        Console.WriteLine("已发 C3H 清零（收尾）");
        await Task.Delay(Math.Max(800, waitMs));
        ReportStatus("⑦ C3 清零后", controller, lastSample, sampleCount);

        Console.WriteLine();
        Console.WriteLine("判读要点：");
        Console.WriteLine("  · ② 正转=True、反转=False；⑤ 反转=True、正转=False；③⑥ 两位都 False。");
        Console.WriteLine("  · ④ 清零前位移应偏离 0（正转已让工作台离开起点），清零后回到 ~0。");
        return 0;
    }

    /// <summary>
    /// 换向实验（会真的动工作台）：正转上升 2 秒，**不停机**直接发反转下降 —— 控制器应当自己
    /// 先发停止、等一会儿、再发反转。每 100ms 采样机器状态位，打印"状态变化序列"，
    /// 从 0x08（正转）到 0x10（反转）之间应当看得到一段 0x00（停）。
    /// </summary>
    private static async Task<int> RunReverseTestAsync(string host, int port, int localPort, int waitMs)
    {
        const double SegmentSeconds = 2.0;

        Console.WriteLine("=== 换向实验（会真的动工作台：请空载、上下留行程余量）===");
        Console.WriteLine(
            $"流程：正转上升 {SegmentSeconds:0.#}s → **不停机**直接发反转下降 {SegmentSeconds:0.#}s → 停止"
        );
        Console.WriteLine("判读：状态序列里 0x08 与 0x10 之间应出现一段 0x00（先停后转）。");
        Console.WriteLine();

        await using var channel = new UdpChannel(new UdpChannelOptions(host, port, localPort));
        await using var controller = new Gr3DeviceController(
            channel,
            pressChannelIndex: 0,
            displacementSource: DisplacementSource.Displace2
        );

        var lastSample = default(TestSample);
        controller.SampleReceived += (_, sample) => lastSample = sample;

        await controller.ConnectAsync();
        Console.WriteLine("联机成功（A0 已收到）");
        await Task.Delay(Math.Max(800, waitMs));

        var clock = Stopwatch.StartNew();
        var lastStatus = (byte)0xFF;

        void Trace()
        {
            var status = (byte)controller.LastMachineStatus;
            if (status != lastStatus)
            {
                lastStatus = status;
                Console.WriteLine(
                    $"  {clock.Elapsed.TotalSeconds,5:0.0}s  机器 0x{status:X2}"
                        + $"（正转={(status & 0x08) != 0} 反转={(status & 0x10) != 0}）"
                        + $"  位移 {lastSample.Displacement,8:F3} mm"
                );
            }
        }

        var start = lastSample.Displacement;

        await controller.JogAsync(JogDirection.Up);
        for (var i = 0; i < (int)(SegmentSeconds * 10); i++)
        {
            await Task.Delay(100);
            Trace();
        }

        // 关键动作：**不停机**，直接换向 —— 控制器应当先发停止、等 500ms、再发反转。
        // 注意：换向那几百毫秒里必须**并发采样**，否则采样循环正好被 await 挡住、看不到 0x00 段。
        var reverseTask = controller.JogAsync(JogDirection.Down);
        for (var i = 0; i < (int)(SegmentSeconds * 10); i++)
        {
            await Task.Delay(100);
            Trace();
        }

        await reverseTask;

        await controller.StopAsync();
        await Task.Delay(400);
        Trace();

        Console.WriteLine($"  上行前位移 {start:F3} → 结束后 {lastSample.Displacement:F3} mm");
        return 0;
    }

    /// <summary>
    /// 运行中改速率（会真的动工作台）：先按 5mm/min 正转，**不停机**再改成 50mm/min，
    /// 分别量两段的位移速率。用来验证"同向运转时只有重发 C4 才改速度"这条语义
    ///（ANX 2026-10-12 之前只改本地字段、不重发 → 速率不生效）。
    /// </summary>
    private static async Task<int> RunSpeedChangeAsync(string host, int port, int localPort, int waitMs)
    {
        const double SlowMillimetersPerMinute = 5.0;
        const double FastMillimetersPerMinute = 50.0;
        const double SegmentSeconds = 4.0;

        Console.WriteLine("=== 运行中改速率（会真的动工作台：请空载、上下留行程余量）===");
        Console.WriteLine(
            $"流程：按试验路径启动（Start：C1 + C4 带 {SlowMillimetersPerMinute:0.#} mm/min）跑 {SegmentSeconds:0.#}s 量速率"
                + $" → **不停机**只改速率到 {FastMillimetersPerMinute:0.#} mm/min 再跑 {SegmentSeconds:0.#}s 量速率"
        );
        Console.WriteLine();

        await using var channel = new UdpChannel(new UdpChannelOptions(host, port, localPort));
        await using var controller = new Gr3DeviceController(
            channel,
            pressChannelIndex: 0,
            displacementSource: DisplacementSource.Displace2
        );

        var lastSample = default(TestSample);
        controller.SampleReceived += (_, sample) => lastSample = sample;

        await controller.ConnectAsync();
        Console.WriteLine("联机成功（A0 已收到）");
        await Task.Delay(Math.Max(800, waitMs));

        await controller.StopAsync();
        await Task.Delay(400);

        var start = lastSample.Displacement;
        // 走试验路径：速率由 SetSpeedAsync 记下，StartAsync 的 C4 带它出去
        //（手动点动那一路用的是 JogSpeedMillimetersPerMinute，不在这里测）
        await controller.SetSpeedAsync(SlowMillimetersPerMinute);
        await controller.StartAsync();

        await Task.Delay(TimeSpan.FromSeconds(SegmentSeconds));
        var slowEnd = lastSample.Displacement;

        // 关键动作：不停机，只改速率 —— 控制器应当自己重发 C4（协议：同向运转时只有重发才改速度）
        await controller.SetSpeedAsync(FastMillimetersPerMinute);
        await Task.Delay(TimeSpan.FromSeconds(SegmentSeconds));
        var fastEnd = lastSample.Displacement;

        await controller.StopAsync();
        await Task.Delay(400);
        var status = controller.LastMachineStatus;

        Console.WriteLine(
            $"  第一段 {SlowMillimetersPerMinute:0.#} mm/min：位移 {start:F4} → {slowEnd:F4} mm"
                + $" → 实测 {(slowEnd - start) / SegmentSeconds * 60:0.###} mm/min"
        );
        Console.WriteLine(
            $"  第二段 {FastMillimetersPerMinute:0.#} mm/min（运行中改）：位移 {slowEnd:F4} → {fastEnd:F4} mm"
                + $" → 实测 {(fastEnd - slowEnd) / SegmentSeconds * 60:0.###} mm/min"
        );
        Console.WriteLine($"  末状态：机器 0x{(byte)status:X2}（应为 0x00，电机已停）");

        // 倒回原位，行程不累积
        await controller.SetSpeedAsync(FastMillimetersPerMinute);
        var backSeconds = (fastEnd - start) / FastMillimetersPerMinute * 60.0 + 0.5;
        await controller.JogAtSpeedAsync(JogDirection.Down, FastMillimetersPerMinute);
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1.0, backSeconds)));
        await controller.StopAsync();
        Console.WriteLine($"  回程后位移 {lastSample.Displacement:F4} mm");

        Console.WriteLine();
        Console.WriteLine(
            "判读：第二段实测应明显大于第一段（≈10 倍）→ 重发 C4 生效；"
                + "若两段速率接近 → 改速度没下发（本修复失效）。"
        );
        return 0;
    }

    /// <summary>
    /// 只读观测会话保活：联机后保持 N 秒，每秒打印收到的帧数；`--heartbeat <秒>` 时周期性发 CBH 心跳。
    /// 用途：确认"PC 沉默多久会被下位机回收会话"（2026-10-11 实测约 60 秒后停流）。
    /// </summary>
    private static async Task<int> RunWatchAsync(
        string host,
        int port,
        int localPort,
        int seconds,
        double heartbeatSeconds,
        bool useCb
    )
    {
        Console.WriteLine(
            $"=== 会话保活观测（只读，{seconds}s"
                + (
                    heartbeatSeconds > 0
                        ? $"，每 {heartbeatSeconds:0.#}s 发一次{(useCb ? "CBH 心跳" : "CAH 读通道配置")}"
                        : "，不发保活帧"
                )
                + "）==="
        );

        await using var channel = new UdpChannel(new UdpChannelOptions(host, port, localPort));
        await using var controller = new Gr3DeviceController(
            channel,
            pressChannelIndex: 0,
            displacementSource: DisplacementSource.Displace2
        );

        var frames = 0;
        var lastSample = default(TestSample);
        controller.SampleReceived += (_, sample) => lastSample = sample;
        channel.DataReceived += (_, e) =>
        {
            var span = e.Data.Span;
            while (Gr3FrameCodec.TryDecodeFrame(span, out DecodedFrame _, out var consumed))
            {
                frames++;
                span = span[consumed..];
            }
        };

        await controller.ConnectAsync();
        Console.WriteLine("联机成功（A0 已收到）");
        Console.WriteLine(
            $"  设备上报量程：压力 {(controller.LoadRangeKN?.ToString("0.###") ?? "无")} kN"
                + $"（通道 {controller.PressChannelDisplayNumber}）"
                + $"   位移 {(controller.DisplacementRangeMillimeters?.ToString("0.###") ?? "无")} mm"
                + $"（通道 {controller.DisplaceChannelDisplayNumber}）"
        );

        var clock = Stopwatch.StartNew();
        var nextBeat = heartbeatSeconds;
        var previousFrames = 0;
        for (var s = 1; s <= seconds; s++)
        {
            await Task.Delay(1000);

            var total = frames;
            var rate = total - previousFrames;
            previousFrames = total;

            Console.WriteLine(
                $"{clock.Elapsed.TotalSeconds,5:0}s  本秒 {rate,4} 帧  累计 {total,7} 帧"
                    + $"  机器 0x{(byte)controller.LastMachineStatus:X2}"
                    + $"  荷载 {lastSample.Load,8:F3} kN"
                    + $"  位移 {lastSample.Displacement,8:F3} mm"
                    + (rate == 0 ? "   ← 本秒没有数据" : string.Empty)
            );

            if (heartbeatSeconds > 0 && clock.Elapsed.TotalSeconds >= nextBeat)
            {
                if (useCb)
                {
                    await controller.HeartbeatAsync();
                }
                else
                {
                    await controller.SendKeepAliveAsync();
                }

                nextBeat += heartbeatSeconds;
            }
        }

        Console.WriteLine($"结束：{seconds}s 内共收 {frames} 帧。");
        return 0;
    }

    /// <summary>
    /// 逐通道原始值诊断（**只读、不动电机**）：打印 A0 的通道配置，以及 A2 每帧的压力 1~4 /
    /// 位移 1~4 / 千分表 1~4 的原始值与工程量，并按适配器口径把四组"双路平均"算出来对照。
    ///
    /// 用途：核对"通道设置里选了位移1+2平均值，界面显示的到底是不是这两路平均"
    /// （用户 2026-10-12 提出；两路若小数位/量纲不同，直接平均就会错）。
    /// </summary>
    private static async Task<int> RunChannelDumpAsync(
        string host,
        int port,
        int localPort,
        int seconds
    )
    {
        Console.WriteLine("=== 逐通道原始值诊断（只读，不动电机）===");

        await using var channel = new UdpChannel(new UdpChannelOptions(host, port, localPort));
        Gr3A2RealtimeData? last = null;
        channel.DataReceived += (_, e) =>
        {
            var span = e.Data.Span;
            while (Gr3FrameCodec.TryDecodeFrame(span, out var frame, out var consumed))
            {
                if (Gr3UplinkParser.TryParseRealtimeData(frame, out var data))
                {
                    last = data;
                }

                span = span[consumed..];
            }
        };

        await using var controller = new Gr3DeviceController(
            channel,
            pressChannelIndex: 0,
            displacementSource: DisplacementSource.DisplaceAverage12
        );
        var resolvedDisplacement = double.NaN;
        controller.SampleReceived += (_, sample) => resolvedDisplacement = sample.Displacement;
        await controller.ConnectAsync();

        if (controller.SystemInfo is not { } info)
        {
            Console.WriteLine("未收到 A0 系统配置，无法换算工程量。");
            return 1;
        }

        Console.WriteLine("A0 通道配置（工程量 = 原始值 / 10^Digital）：");
        for (var i = 0; i < info.PressChannels.Count; i++)
        {
            var c = info.PressChannels[i];
            Console.WriteLine(
                $"  压力通道{i + 1}：Scale={c.Scale,6} Digital={c.Digital} Unit={c.UnitCode}"
            );
        }

        for (var i = 0; i < info.DisplaceChannels.Count; i++)
        {
            var c = info.DisplaceChannels[i];
            Console.WriteLine(
                $"  位移通道{i + 1}：Scale={c.Scale,6} Digital={c.Digital} Unit={c.UnitCode}"
                    + $" → 换算量程 {c.ToEngineeringValue(c.Scale):0.###}"
            );
        }

        Console.WriteLine($"千分表有效位：{string.Join(",", info.MicrometerValid)}（原始值 /1000 = mm）");
        Console.WriteLine();

        for (var s = 1; s <= seconds; s++)
        {
            await Task.Delay(1000);

            if (last is not { } d)
            {
                Console.WriteLine($"{s,3}s  还没收到 A2 实时帧");
                continue;
            }

            Console.WriteLine($"{s,3}s  机器 0x{(byte)d.MachineStatus:X2}  试验状态 0x{(byte)d.TestStatus:X2}");
            Console.WriteLine(
                "     压力 1~4 原始："
                    + string.Join(" ", d.PressData.Select((raw, i) => $"{i + 1}={raw}"))
            );
            Console.WriteLine(
                "     位移 1~4 原始："
                    + string.Join(" ", d.DisplaceData.Select((raw, i) => $"{i + 1}={raw}"))
            );
            Console.WriteLine(
                "     位移 1~4 工程量："
                    + string.Join(
                        " ",
                        d.DisplaceData.Select(
                            (raw, i) =>
                                $"{i + 1}={Disp(info, i, raw):0.####}"
                        )
                    )
            );
            Console.WriteLine(
                "     千分表 1~4 原始："
                    + string.Join(" ", d.MicrometerData.Select((raw, i) => $"{i + 1}={raw}"))
            );
            Console.WriteLine(
                "     适配器口径：位移1+2平均="
                    + $"{Average(info, d.DisplaceData, 0, 1):0.####}"
                    + $"  位移3+4平均={Average(info, d.DisplaceData, 2, 3):0.####}"
                    + $"  千分表1+2平均={(d.MicrometerData[0] + d.MicrometerData[1]) / 2000.0:0.####}"
                    + $"  千分表3+4平均={(d.MicrometerData[2] + d.MicrometerData[3]) / 2000.0:0.####}"
            );
            Console.WriteLine(
                "     单路对照：位移1="
                    + $"{Disp(info, 0, d.DisplaceData[0]):0.####}"
                    + $"  位移2={Disp(info, 1, d.DisplaceData[1]):0.####}"
                    + $"  控制器算出(位移1+2平均)={resolvedDisplacement:0.####}"
            );
        }

        return 0;

        static double Disp(Gr3A0SystemInfo info, int index, int raw) =>
            index < info.DisplaceChannels.Count
                ? info.DisplaceChannels[index].ToEngineeringValue(raw)
                : raw;

        static double Average(Gr3A0SystemInfo info, IReadOnlyList<int> data, int a, int b) =>
            (Disp(info, a, data[a]) + Disp(info, b, data[b])) / 2.0;
    }

    /// <summary>
    /// 速率标定（会真的动工作台）：按现役的换算请求若干速率，实测位移速率，反推
    /// 「速度字段 1 个单位 = 多少 mm/min」。每档正转之后反转向下同样时长，行程不累积。
    /// </summary>
    private static async Task<int> RunSpeedCalibrationAsync(string host, int port, int localPort, int waitMs)
    {
        // (请求速率 mm/min, 保持秒数)。请求速率经 Gr3MotorSpeed.ToWire 换算成下发字段值。
        var steps = new (double MmPerMin, double Seconds)[]
        {
            (5.0, 6.0),
            (20.0, 3.0),
            (50.0, 1.5),
        };

        Console.WriteLine("=== 速率标定（会真的动工作台：请空载、上下留行程余量）===");
        Console.WriteLine($"远端 {host}:{port}   本地端口 {localPort}");
        Console.WriteLine(
            $"换算假设：下发值 = 速率 × {Gr3MotorSpeed.UnitsPerMillimeterPerMinute:0.#}"
                + $"（1 单位 = {1 / Gr3MotorSpeed.UnitsPerMillimeterPerMinute:0.###} mm/min，现役值）"
        );
        Console.WriteLine(
            $"⚠ 电机上限 {Gr3MotorSpeed.MaxMillimetersPerMinute:0.#} mm/min，超过会让伺服宕机；"
                + "ToWire 会截断到上限，所以本模式不会超速。"
        );
        Console.WriteLine();

        await using var channel = new UdpChannel(new UdpChannelOptions(host, port, localPort));
        await using var controller = new Gr3DeviceController(
            channel,
            pressChannelIndex: 0,
            displacementSource: DisplacementSource.Displace2
        );

        var lastSample = default(TestSample);
        controller.SampleReceived += (_, sample) => lastSample = sample;

        // 顺手留一份 A2 原始数据域：点动期间的「试验状态」字节跟协议定义对不上（读到 0x03 / 0x04），
        // 打出来才好定位是字节偏移错了还是位定义不同。
        byte[]? lastRealtimePayload = null;
        channel.DataReceived += (_, e) =>
        {
            var span = e.Data.Span;
            while (Gr3FrameCodec.TryDecodeFrame(span, out var frame, out var consumed))
            {
                if (frame.CommandCode == Gr3Constants.UplinkRealtimeData)
                {
                    lastRealtimePayload = frame.Payload.ToArray();
                }

                span = span[consumed..];
            }
        };

        try
        {
            await controller.ConnectAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"联机失败：{ex.Message}");
            return 2;
        }

        Console.WriteLine("联机成功（A0 已收到）");
        await Task.Delay(Math.Max(800, waitMs));
        Console.WriteLine(
            $"  起始：机器状态 0x{(byte)controller.LastMachineStatus:X2}   位移 {lastSample.Displacement:F4} mm"
        );
        Console.WriteLine();

        foreach (var (mmPerMin, seconds) in steps)
        {
            var wire = Gr3MotorSpeed.ToWire(mmPerMin);

            var up = await MeasureAsync(controller, () => lastSample, JogDirection.Up, mmPerMin, seconds);
            PrintStep($"正转上升 请求 {mmPerMin:0.##} mm/min（下发 {wire}）", up.Delta, seconds);

            var down = await MeasureAsync(controller, () => lastSample, JogDirection.Down, mmPerMin, seconds);
            PrintStep($"反转下降 请求 {mmPerMin:0.##} mm/min（下发 {wire}）", down.Delta, seconds);

            Console.WriteLine();

            if (up.HitLimit || down.HitLimit)
            {
                Console.WriteLine("撞限位，标定中止 —— 请人工把工作台挪回中间再重跑。");
                return 3;
            }
        }

        if (lastRealtimePayload is { } payload)
        {
            Console.WriteLine("点动期间最后一帧 A2 数据域（71 B）：");
            Console.WriteLine("  " + ToHex(payload));
        }

        Console.WriteLine();
        Console.WriteLine("判读：实测速率 ≈ 请求速率 → 换算正确；差固定倍数（如 1/10、1/100）→ 单位选错。");
        return 0;
    }

    /// <summary>点动指定时长，返回位移变化量与是否撞限位；结束即停止电机。</summary>
    private static async Task<(double Delta, bool HitLimit)> MeasureAsync(
        Gr3DeviceController controller,
        Func<TestSample> latest,
        JogDirection direction,
        double millimetersPerMinute,
        double seconds
    )
    {
        await controller.StopAsync();
        await Task.Delay(400);

        var before = latest().Displacement;
        await controller.JogAtSpeedAsync(direction, millimetersPerMinute);
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var after = latest().Displacement;

        await controller.StopAsync();
        await Task.Delay(400);

        var status = controller.LastMachineStatus;
        var hitLimit =
            status.HasFlag(Gr3MachineStatus.DisplacementUpperLimit)
            || status.HasFlag(Gr3MachineStatus.DisplacementLowerLimit);

        if (hitLimit)
        {
            Console.WriteLine($"  ⚠ 撞限位（机器状态 0x{(byte)status:X2}）");
        }

        return (after - before, hitLimit);
    }

    private static void PrintStep(string label, double deltaMillimeters, double seconds)
    {
        var rate = deltaMillimeters / seconds * 60.0;
        Console.WriteLine(
            $"  {label}：{seconds:0.#}s 位移变化 {deltaMillimeters:+0.0000;-0.0000;0.0000} mm"
                + $" → 实测 {rate:0.###} mm/min"
        );
    }

    /// <summary>打印最近一帧 A2 的状态位 + 荷载 / 位移。</summary>
    private static void ReportStatus(
        string label,
        Gr3DeviceController controller,
        TestSample sample,
        int sampleCount
    )
    {
        var status = controller.LastMachineStatus;

        Console.WriteLine(
            $"  {label}：机器状态 0x{(byte)status:X2}"
                + $"（压力过载={status.HasFlag(Gr3MachineStatus.PressureOverload)}"
                + $" 上限位={status.HasFlag(Gr3MachineStatus.DisplacementUpperLimit)}"
                + $" 下限位={status.HasFlag(Gr3MachineStatus.DisplacementLowerLimit)}"
                + $" 正转={status.HasFlag(Gr3MachineStatus.MotorForward)}"
                + $" 反转={status.HasFlag(Gr3MachineStatus.MotorReverse)}"
                + $" 控温={status.HasFlag(Gr3MachineStatus.TemperatureControlActive)}）"
                + $"  试验状态 0x{(byte)controller.LastTestStatus:X2}"
        );
        Console.WriteLine(
            $"     荷载 {sample.Load:F4} kN   位移 {sample.Displacement:F4} mm" + $"   累计采样 {sampleCount} 帧"
        );
    }

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
