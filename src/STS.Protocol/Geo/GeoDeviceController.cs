namespace STS.Protocol.Geo;

using STS.Core;
using STS.Core.Communication;
using STS.Core.Control;

/// <summary>
/// 土工（全自动土工项目）协议设备控制器 —— **骨架占位**。
///
/// 已就位：串口通道（<see cref="ICommChannel"/>）与联机 / 断开。
/// 下一步（周内）：按《全自动土工项目通讯协议 V1.7》实现帧层
/// （`FA FB FC` + 长度 + 命令 + 数据 + CRC-8）、上行解析与下行指令；
/// V1.7 相对 V1.6 新增的"体积变化量上传"指令**独立成小块**；
/// 先用文档黄金用例 + 模拟下位机验收，真机联调待设备到位。
/// </summary>
public sealed class GeoDeviceController(ICommChannel channel) : ITestDeviceController
{
    public string DeviceName => $"土工三轴仪（{channel.DisplayName}）";

    public bool IsConnected { get; private set; }

    public double? DisplacementRangeMillimeters => null;

    public double? LoadRangeKN => null;

    public bool? IsMotorRunning => null;

    public double ElapsedSeconds { get; set; }

    public double JogSpeedMillimetersPerMinute { get; set; }

    public int PressChannelNumber { get; set; }

    public DisplacementSource DisplacementSource { get; set; } = DisplacementSource.Displace2;

#pragma warning disable CS0067 // 骨架：帧层实现后由上行解析推送采样
    public event EventHandler<TestSample>? SampleReceived;
#pragma warning restore CS0067

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await channel.OpenAsync(cancellationToken).ConfigureAwait(false);
        IsConnected = true;
    }

    public async Task DisconnectAsync()
    {
        await channel.CloseAsync().ConfigureAwait(false);
        IsConnected = false;
    }

    public Task SetSpeedAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) => throw Skeleton(nameof(SetSpeedAsync));

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        throw Skeleton(nameof(StartAsync));

    public Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default) =>
        throw Skeleton(nameof(JogAsync));

    public Task ReturnToStartAsync(
        double millimetersPerMinute,
        CancellationToken cancellationToken = default
    ) => throw Skeleton(nameof(ReturnToStartAsync));

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        throw Skeleton(nameof(StopAsync));

    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        throw Skeleton(nameof(ResetAsync));

    public Task ZeroDisplacementAsync(CancellationToken cancellationToken = default) =>
        throw Skeleton(nameof(ZeroDisplacementAsync));

    private static NotSupportedException Skeleton(string member) =>
        new($"土工协议实现中（骨架占位）：{member} 将在协议帧层落地后接通。");
}
