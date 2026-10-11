namespace ANX.Core.Control;

/// <summary>
/// 试验设备抽象。框架只认这些动作，不认识任何机型命令 ——
/// 机型差异（帧怎么编、速度怎么换算）留在 <see cref="Protocol.IFrameCodec"/> 的实现里。
/// </summary>
public interface ITestDeviceController
{
    /// <summary>设备名（用于界面与日志）。</summary>
    string DeviceName { get; }

    /// <summary>是否已联机。</summary>
    bool IsConnected { get; }

    /// <summary>
    /// 试验已进行的秒数（接触清零后起算）。**由控制循环每拍推进** —— A2H 帧本身不带时间戳，
    /// 阶段机的超时与复位计时都以它为准（与现役线程用自己秒表计时同义）。
    /// </summary>
    double ElapsedSeconds { get; set; }

    /// <summary>实时采样快照。由控制引擎订阅，用于阶段判断、保护与曲线记录。</summary>
    event EventHandler<TestSample>? SampleReceived;

    /// <summary>联机（下位机进入联机模式后才会回送数据）。</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开联机。</summary>
    Task DisconnectAsync();

    /// <summary>设定加载速率（mm/min）。</summary>
    Task SetSpeedAsync(double millimetersPerMinute, CancellationToken cancellationToken = default);

    /// <summary>启动加载。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 点动工作台（手动上升 / 下降）。**按住运行、松开停止**由界面实现：
    /// 按下时调本方法，松开时调 <see cref="StopAsync"/>。
    /// </summary>
    Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default);

    /// <summary>停止电机。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>回零 / 复位。</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
