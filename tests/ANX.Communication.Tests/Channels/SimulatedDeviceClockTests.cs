namespace ANX.Communication.Tests.Channels;

using System.Diagnostics;
using ANX.Communication.Channels;
using ANX.Core;

/// <summary>
/// 模拟器的**仿真时钟**（用户 2026-10-15："模拟曲线右端不要延长那么多"）：
/// 时间倍率让自检跑得比墙钟快，但它必须**同时**作用在"位移"和"时间戳"上 ——
/// 只推位移不推时钟的话，上位机的阶段机（接触超时 / 破坏后继续加载时间）按墙钟计时，
/// 曲线却按仿真时间走位移，两边的"10 秒"不是一个长度：真机 2mm/min 下"继续加载 10s"是
/// 0.33mm，模拟数据却记成 1.67mm，曲线右端拖得极长。
/// </summary>
public sealed class SimulatedDeviceClockTests
{
    [Fact]
    public async Task 仿真时钟与位移同步_且比墙钟快()
    {
        const double interval = 0.01;
        const double timeScale = 5.0;

        await using var device = new SimulatedDeviceController(
            sampleIntervalSeconds: interval,
            timeScale: timeScale
        );
        await device.ConnectAsync();

        var samples = new List<TestSample>();
        device.SampleReceived += (_, sample) => samples.Add(sample);

        var wall = Stopwatch.StartNew();
        await device.StartAsync();
        await Task.Delay(250);
        await device.StopAsync();
        wall.Stop();

        Assert.NotEmpty(samples);

        // ① 时钟走过的时间比墙钟多（倍率生效）
        Assert.True(
            device.ElapsedSeconds > wall.Elapsed.TotalSeconds * 2,
            $"仿真时钟应当跑得比墙钟快：仿真 {device.ElapsedSeconds:0.###}s / 墙钟 {wall.Elapsed.TotalSeconds:0.###}s"
        );

        // ② 位移与时钟**同一个时钟**：默认 50mm/min，行程 = 速率 × 仿真时间
        var expectedTravel = 50.0 / 60.0 * device.ElapsedSeconds;
        Assert.Equal(expectedTravel, samples[^1].RawDisplacement, 0.05);
    }
}
