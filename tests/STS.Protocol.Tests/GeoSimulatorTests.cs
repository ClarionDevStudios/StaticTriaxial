namespace STS.Protocol.Tests;

using STS.Protocol.Geo;

/// <summary>模拟下位机：协议应答（C0/C1/C2/C3 → A0/A2）+ A1 流与 T1236 形状的推进。</summary>
public sealed class GeoSimulatorTests
{
    [Fact]
    public void 联机应回A0系统配置()
    {
        var simulator = new GeoSimulator();

        var frame = Assert.Single(Decode(simulator.Feed(GeoCommands.Connect(order: 1))));

        Assert.True(GeoSystemConfig.TryParse(frame, out var config));
        Assert.NotNull(config);
        Assert.Equal(1.7, config.ProtocolVersion, 9);
        Assert.Equal(1.7, config.FirmwareVersion, 9);
        Assert.True(config.Press3Valid);
        Assert.False(config.Displacement3Valid);
        Assert.True(simulator.IsOnline);
    }

    [Fact]
    public void 未联机就要求主动推送应被拒绝()
    {
        var simulator = new GeoSimulator();

        var frame = Assert.Single(Decode(simulator.Feed(GeoCommands.Connect(order: 2))));

        Assert.True(GeoAck.TryParse(frame, out var ack));
        Assert.Equal(GeoAckStatus.CannotExecute, ack!.Status);
        Assert.False(simulator.IsStreaming);
    }

    [Fact]
    public void 联机后应按拍产出A1且位移随电机推进()
    {
        var simulator = new GeoSimulator();
        _ = simulator.Feed(GeoCommands.Connect(order: 1));
        _ = simulator.Feed(GeoCommands.Connect(order: 2));
        _ = simulator.Feed(GeoCommands.Motor(GeoCommands.MotorOrder.Up, 4590)); // 4.59mm/min

        var sample = ParseSample(simulator.Tick(1.0));

        Assert.True(sample.MotorUp);
        Assert.False(sample.TestRunning);
        // 10mm + 4.59/60 = 10.0765mm → 原始值 ≈ 10076/10077（0.001mm）
        Assert.InRange(sample.Displacement1Raw, 10075, 10079);
    }

    [Fact]
    public void 围压应缓升到目标并置稳压位()
    {
        var simulator = new GeoSimulator();
        _ = simulator.Feed(GeoCommands.Connect(order: 1));
        _ = simulator.Feed(GeoCommands.Connect(order: 2));
        _ = simulator.Feed(
            GeoCommands.ConfiningPressure(30, 0, true, false, 400, 0, 50, 19635)
        );

        var ramping = ParseSample(simulator.Tick(0.1)); // 100kPa/s → 10kPa
        Assert.InRange(ramping.Press3Raw, 9, 11);
        Assert.False(ramping.ConfiningStable);

        var stable = ParseSample(simulator.Tick(0.2)); // 再 20kPa → 到 30kPa
        Assert.Equal(30, stable.Press3Raw);
        Assert.True(stable.ConfiningStable);
    }

    [Fact]
    public void 试验状态与加载曲线应符合模拟约定()
    {
        var simulator = new GeoSimulator();
        _ = simulator.Feed(GeoCommands.Connect(order: 1));
        _ = simulator.Feed(GeoCommands.Connect(order: 2));
        _ = simulator.Feed(GeoCommands.Motor(GeoCommands.MotorOrder.Up, 4590));
        _ = simulator.Feed(GeoCommands.TestState(action: 1));

        var sample = ParseSample(simulator.Tick(6.0));

        Assert.True(sample.TestRunning);
        Assert.True(sample.MotorUp);
        // 6s → Δh=0.459mm → 荷载 0.16×0.459≈0.0734kN → 原始值 ≈73（0.001kN）
        Assert.InRange(sample.Press1Raw, 72, 75);
    }

    [Fact]
    public void 未知命令应回无法执行()
    {
        var simulator = new GeoSimulator();

        var frame = Assert.Single(Decode(simulator.Feed(GeoFrameCodec.Build(0xC7, [0x00]))));

        Assert.True(GeoAck.TryParse(frame, out var ack));
        Assert.Equal(GeoAckStatus.CannotExecute, ack!.Status);
    }

    private static List<GeoFrame> Decode(byte[] bytes) =>
        [.. new GeoFrameAssembler().Feed(bytes)];

    private static GeoRealtimeSample ParseSample(byte[] bytes)
    {
        var frame = Assert.Single(Decode(bytes));
        Assert.True(GeoRealtimeSample.TryParse(frame, out var sample));

        return sample!;
    }
}
