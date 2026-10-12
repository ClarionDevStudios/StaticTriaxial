namespace ANX.Control;

using ANX.Core.Control;

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

    /// <summary>
    /// 荷载闸门（kN）。运行前由"**设备压力量程 × 安全系数**"算出（用户 2026-10-12 定的口径，
    /// 见 <see cref="ClampLoadToDeviceRange"/>）；设备不上报量程时用 <see cref="FallbackMaxLoadKN"/>。
    /// </summary>
    public double MaxLoadKN { get; init; } = 50.0;

    /// <summary>取不到设备压力量程时的回落值（kN）—— 沿用现役 `MAX_LOAD_LIMIT_KN = 50.0`。</summary>
    public const double FallbackMaxLoadKN = 50.0;

    /// <summary>
    /// 荷载闸门的安全系数（× 设备压力量程），默认 0.95 —— 真机 100.000kN 量程 → 95kN。
    /// 由方法从**参数表**给出（用户 2026-10-12 要求可调），取值被夹在 0.1~1.0。
    /// </summary>
    public double LoadRangeSafetyFactor { get; init; } = 0.95;

    /// <summary>位移正向行程上限（mm）。</summary>
    public double MaxDisplacementMM { get; init; } = 50.0;

    /// <summary>
    /// 位移闸门的安全系数：闸门取设备量程的这个比例（默认 0.95，即留 5% 余量）。
    /// **顶到量程末端会压坏位移传感器**（用户 2026-10-11 明确，现场量程 25.000mm），所以要留余量；
    /// 系数由方法从**参数表**给出（用户 2026-10-12 要求可调），取值被夹在 0.1~1.0（>1 就等于放开保护）。
    /// </summary>
    public double DisplacementRangeSafetyFactor { get; init; } = 0.95;

    /// <summary>
    /// 把位移闸门收敛到设备量程的安全范围内。`deviceRangeMillimeters` 为 null（设备不上报）时原样返回。
    /// 现场教训：闸门曾写死 50mm，而真机位移量程只有 25mm —— 空载时软件一直往上顶，
    /// 靠操作员手动停机才没顶坏传感器。
    /// </summary>
    public TestRunProfile ClampDisplacementToDeviceRange(double? deviceRangeMillimeters)
    {
        var limit =
            deviceRangeMillimeters is { } range && range > 0
                ? range * Math.Clamp(DisplacementRangeSafetyFactor, 0.1, 1.0)
                : double.PositiveInfinity;

        return MaxDisplacementMM <= limit ? this : this with { MaxDisplacementMM = limit };
    }

    /// <summary>
    /// 按设备压力量程算荷载闸门：闸门 = **量程 × 安全系数**（默认 100kN × 0.95 = 95kN）；
    /// 设备不上报量程（null 或 ≤0）时回落到 <see cref="FallbackMaxLoadKN"/> = 50kN（现役常量）。
    /// </summary>
    public TestRunProfile ClampLoadToDeviceRange(double? deviceRangeKN)
    {
        var gate =
            deviceRangeKN is { } range && range > 0
                ? range * Math.Clamp(LoadRangeSafetyFactor, 0.1, 1.0)
                : FallbackMaxLoadKN;

        return this with { MaxLoadKN = gate };
    }

    /// <summary>接触阶段超时（s）。</summary>
    public double ContactTimeoutSeconds { get; init; } = 300.0;

    /// <summary>加载阶段超时（s）。</summary>
    public double LoadingTimeoutSeconds { get; init; } = 600.0;

    /// <summary>
    /// 破坏后继续加载时长（s）。取参数表「破坏后继续加载时间」（现役 ContinueLoadingTime；单位秒）。
    /// 0 = 不做继续加载（破坏确认后直接归位）。这一段的数据**记入曲线**。
    /// </summary>
    public double ContinueLoadingSeconds { get; init; }

    /// <summary>
    /// **不判破坏荷载下限**（kN）：峰值荷载小于该值时不判破坏（现役弯曲线程常量
    /// <c>BREAK_MIN_LOAD_KN = 0.2</c>，判据写的是 <c>MaxLoad &gt;= BREAK_MIN_LOAD_KN</c>；现场配 200N）。
    /// 0 = 不启用下限。
    /// </summary>
    public double MinBreakLoadKN { get; init; }

    /// <summary>
    /// 试验完成后工作台归位时长（s）。取参数表「试验完成后归位时间」（现役 AutoFallTime）。
    /// 生效时点：破坏确认 → 破坏后继续加载 → 归位倒计时（归位倒计时从继续加载走完才开始算）。
    /// 归位段**不记曲线**（曲线只到破坏后的下降趋势为止）。
    /// </summary>
    public double ResetSeconds { get; init; } = 5.0;

    /// <summary>轴向压力通道号（1~4；1 = 100kN 主传感器）。运行前推给设备适配器。</summary>
    public int PressChannelNumber { get; init; } = 1;

    /// <summary>轴向位移来源（单路 / 双路平均 / 千分表）。运行前推给设备适配器。</summary>
    public DisplacementSource DisplacementSource { get; init; } = DisplacementSource.Displace2;

    /// <summary>试验加载速率（mm/min），规范 50；由方法按"加载速率"参数给出。</summary>
    public double LoadRateMillimetersPerMinute { get; init; } = 50.0;

    /// <summary>
    /// 接触阶段的接近速率（mm/min），默认 10 —— 比试验速率慢，避免压头冲击试件
    /// （与现役线程的 `CONTACT_SPEED_MM_MIN` 同义）。
    /// </summary>
    public double ApproachSpeedMillimetersPerMinute { get; init; } = 10.0;

    /// <summary>
    /// **平台升降速率**（mm/min）：执行页"工作台上升 / 下降"按钮用（用户 2026-10-12 要求把
    /// 平台升降与试验过程分成两个速率）。由方法从参数表装配，受电机上限 50mm/min 约束。
    /// </summary>
    public double JogSpeedMillimetersPerMinute { get; init; } = 50.0;
}
