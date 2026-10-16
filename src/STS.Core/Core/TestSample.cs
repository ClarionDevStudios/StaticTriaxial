namespace STS.Core;

/// <summary>
/// 一个控制周期收到的实时采样快照。单位跟随工地习惯：荷载 kN、位移 mm、时间 s。
/// </summary>
/// <param name="Load">荷载（kN）。</param>
/// <param name="Displacement">
/// 位移**净值**（mm）：已减去"接触调零"的偏移量（现役 `NetValue = FloatValue − ZeroPoint`，
/// 用户 2026-10-12 明确接触清零走上位机软件调零）。曲线与显示用这个值。
/// </param>
/// <param name="ElapsedSeconds">接触清零后起算的秒数。</param>
public readonly record struct TestSample(double Load, double Displacement, double ElapsedSeconds)
{
    /// <summary>
    /// **原始位移**（mm）：未减接触调零偏移的读数（现役的 `AxialOriginalDisplacement`）。
    /// **位移量程保护按它判** —— 保护对象是传感器的绝对位置，拿净值判会漏掉"接触点已经很靠近量程"的情况。
    /// 未设置时与 <see cref="Displacement"/> 相同。
    /// </summary>
    public double RawDisplacement { get; init; } = Displacement;

    /// <summary>
    /// 围压 σ3（kPa；取自 A1 的 PRESS3，原始值单位 0.001MPa 即 1kPa）。
    /// 设备不报 / 未安装围压通道时为 null（界面上显示"—"）。T 1236 的分段流程用它做"稳压"指示。
    /// </summary>
    public double? ConfiningPressureKPa { get; init; }

    /// <summary>围压是否已稳压（A1 的 SATAE Bit10；下位机自己稳压，上位机只显示）。未知为 null。</summary>
    public bool? ConfiningStable { get; init; }
}
