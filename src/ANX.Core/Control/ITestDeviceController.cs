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
    /// 位移通道量程（mm，来自设备配置 A0H 的"位移通道量程"字段）。**试验的位移闸门不得超过它**：
    /// 顶过传感器量程会压坏位移传感器（用户 2026-10-11 明确，现场为 25.000mm）。
    /// 设备不上报时为 null。
    /// </summary>
    double? DisplacementRangeMillimeters { get; }

    /// <summary>
    /// 压力通道量程（kN，轴向压力用）。荷载闸门按它 × 安全系数算（用户 2026-10-12 定的口径）；
    /// 设备不上报时为 null，此时由调用方回落到常量（现役的 50kN）。
    /// </summary>
    double? LoadRangeKN { get; }

    /// <summary>
    /// 电机是否在运转（取自机器状态位 Bit3/Bit4）。命令在加载而这里连续为 false，
    /// 说明设备被手动停止 / 撞限位 / 伺服保护 —— 软件必须知道，不能继续空等。
    /// 设备不上报时为 null。
    /// </summary>
    bool? IsMotorRunning { get; }

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

    /// <summary>
    /// 设定加载速率（mm/min）。**电机正在运转时会同时把新速率下发**（协议规定同向运转时
    /// 只有重发电机指令才改速度，现役 `SetVelocityCmd` 同样每次重发）—— 否则"接触后切到试验速率"不生效。
    /// </summary>
    Task SetSpeedAsync(double millimetersPerMinute, CancellationToken cancellationToken = default);

    /// <summary>启动加载。</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 点动工作台（手动上升 / 下降）。**按住运行、松开停止**由界面实现：
    /// 按下时调本方法，松开时调 <see cref="StopAsync"/>。
    /// </summary>
    Task JogAsync(JogDirection direction, CancellationToken cancellationToken = default);

    /// <summary>
    /// **平台升降速率**（mm/min）—— 点动用。与试验加载速率分开（用户 2026-10-12 要求两个速率），
    /// 由调用方从参数表取好再赋值；受电机上限约束（换算入口会截断）。
    /// </summary>
    double JogSpeedMillimetersPerMinute { get; set; }

    /// <summary>停止电机。</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回零 / 复位（GR-3 走 C3H 硬件清零）。**注意与 <see cref="ZeroDisplacementAsync"/> 的区别**：
    /// 复位是"设备侧清零"，接触调零是"上位机侧存偏移量"（用户 2026-10-12 明确接触清零按现役做法）。
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接触调零（**上位机软件调零**，存偏移量）：把**当前位移读数**记为偏移量，
    /// 之后采样净值为 0 起算（净值 = 原始 − 偏移），**不动设备零点**。
    /// 口径依据现役 `AnalyzeDataController.ZeroAxialDisplacement`（接触成立时调位移，不调荷载）。
    /// </summary>
    Task ZeroDisplacementAsync(CancellationToken cancellationToken = default);
}
