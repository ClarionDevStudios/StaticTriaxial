namespace ANX.Control;

/// <summary>
/// 一次试验的运行参数与保护阈值。按既定分工：**软件只管正向量程与超时**，
/// 负方向与硬件限位由下位机负责（下位机限位标识不准，软件不采用）。
/// </summary>
public sealed record TestRunProfile
{
    /// <summary>接触判定阈值（kN）：荷载达到它才算接触，之后才开始记录曲线。</summary>
    public double ContactLoadKN { get; init; } = 0.05;

    /// <summary>破坏判据：荷载自峰值下降这么多（kN）即视为破坏。</summary>
    public double BreakDropKN { get; init; } = 0.3;

    /// <summary>破坏确认所需连续采样帧数（抗单帧毛刺）。</summary>
    public int BreakConfirmSamples { get; init; } = 3;

    /// <summary>荷载量程上限（kN）。</summary>
    public double MaxLoadKN { get; init; } = 50.0;

    /// <summary>位移正向行程上限（mm）。</summary>
    public double MaxDisplacementMM { get; init; } = 50.0;

    /// <summary>接触阶段超时（s）。</summary>
    public double ContactTimeoutSeconds { get; init; } = 300.0;

    /// <summary>加载阶段超时（s）。</summary>
    public double LoadingTimeoutSeconds { get; init; } = 600.0;

    /// <summary>破坏后自动复位时长（s）。</summary>
    public double ResetSeconds { get; init; } = 5.0;

    /// <summary>试验加载速率（mm/min），规范 50；由方法按"加载速率"参数给出。</summary>
    public double LoadRateMillimetersPerMinute { get; init; } = 50.0;

    /// <summary>
    /// 接触阶段的接近速率（mm/min），默认 10 —— 比试验速率慢，避免压头冲击试件
    /// （与现役线程的 `CONTACT_SPEED_MM_MIN` 同义）。
    /// </summary>
    public double ApproachSpeedMillimetersPerMinute { get; init; } = 10.0;
}
