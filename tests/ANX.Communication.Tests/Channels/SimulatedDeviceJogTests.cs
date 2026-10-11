namespace ANX.Communication.Tests.Channels;

using ANX.Communication.Channels;
using ANX.Core;
using ANX.Core.Control;

/// <summary>
/// 模拟器的点动（对应执行页"工作台上升 / 下降"按钮按住时发的那条命令）：
/// 上升位移增、下降位移减且不越过 0，空载荷载≈0；松开（StopAsync）后不再来采样。
/// </summary>
public sealed class SimulatedDeviceJogTests
{
    [Fact]
    public async Task 按住上升应让位移增大而荷载近似为空载()
    {
        await using var device = new SimulatedDeviceController(sampleIntervalSeconds: 0.005);
        await device.ConnectAsync();

        var samples = new List<TestSample>();
        device.SampleReceived += (_, sample) => samples.Add(sample);

        await device.JogAsync(JogDirection.Up);
        await Task.Delay(120);
        await device.StopAsync();

        Assert.NotEmpty(samples);
        Assert.True(
            samples[^1].Displacement > samples[0].Displacement,
            $"上升时位移应增大：{samples[0].Displacement} → {samples[^1].Displacement}"
        );
        Assert.All(samples, sample => Assert.True(sample.Load < 0.05, $"空载荷载应≈0，实际 {sample.Load}"));
    }

    [Fact]
    public async Task 松开后不再采样且下降不会越过零()
    {
        await using var device = new SimulatedDeviceController(sampleIntervalSeconds: 0.005);
        await device.ConnectAsync();

        var samples = new List<TestSample>();
        device.SampleReceived += (_, sample) => samples.Add(sample);

        // 先升一点
        await device.JogAsync(JogDirection.Up);
        await Task.Delay(100);
        await device.StopAsync();

        var afterStop = samples.Count;
        await Task.Delay(80);
        Assert.Equal(afterStop, samples.Count);

        // 再下降：位移应减小，且不会到负数
        await device.JogAsync(JogDirection.Down);
        await Task.Delay(300);
        await device.StopAsync();

        var peak = samples.Max(sample => sample.Displacement);
        Assert.True(samples[^1].Displacement < peak, "下降时位移应减小");
        Assert.All(samples, sample => Assert.True(sample.Displacement >= 0, "位移不应为负"));
    }
}
