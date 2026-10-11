namespace ANX.Protocol.Gr3;

/// <summary>
/// C4H / C5H「速度」字段的换算 —— **唯一入口**，各试验线程、界面点动都走这里。
/// </summary>
/// <remarks>
/// 协议文档只写「速度」，单位靠固件约定。现役 `Common\GR3CommandDispatcher.pas` 的结论是：
/// <list type="bullet">
///   <item>当前固件一位小数：1 单位 = 0.1 mm/min，即 下发值 = 速率(mm/min) × 10；</item>
///   <item>固件升级到两位小数：1 单位 = 0.01 mm/min，即 × 100（现役注释里写明"升级固件时只改常量"）。</item>
/// </list>
/// 现役把这件事收在常量 `MOTOR_SPEED_UNITS_PER_MM_MIN` 里，我们照做，免得各处各乘系数。
/// </remarks>
public static class Gr3MotorSpeed
{
    /// <summary>1 mm/min 对应多少个「速度字段」单位（现役值 10 → 一个单位 = 0.1 mm/min）。</summary>
    public const double UnitsPerMillimeterPerMinute = 10.0;

    /// <summary>手动点动的加速倍率：现役点动 = 试验速率 × 本倍率（"快速调整试样位置"）。</summary>
    public const double JogMultiplier = 10.0;

    /// <summary>速率(mm/min) → C4/C5 速度字段值（Uint16，超上限截断）。</summary>
    public static ushort ToWire(double millimetersPerMinute)
    {
        if (millimetersPerMinute <= 0)
        {
            return 0;
        }

        var value = Math.Round(millimetersPerMinute * UnitsPerMillimeterPerMinute);
        return value >= ushort.MaxValue ? ushort.MaxValue : (ushort)value;
    }

    /// <summary>速度字段值 → 速率(mm/min)（用于显示 / 核对 / 标定）。</summary>
    public static double FromWire(ushort wireValue) => wireValue / UnitsPerMillimeterPerMinute;

    /// <summary>手动点动速率：试验速率 × <see cref="JogMultiplier"/> → 速度字段值。</summary>
    public static ushort JogToWire(double testSpeedMillimetersPerMinute) =>
        ToWire(testSpeedMillimetersPerMinute * JogMultiplier);
}
