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
    /// <summary>
    /// 电机最高速率（mm/min）—— **硬上限，超了伺服会宕机**（用户 2026-10-11 明确）。
    /// 各试验速率、点动速率都不得超过它；<see cref="ToWire"/> 会静默截断到这里，
    /// 免得某处参数写错就把伺服打停。
    /// </summary>
    /// <remarks>
    /// 现场实测（2026-10-11）：按 ×10 换算出 200 mm/min 的字段值下发后，工作台**完全不动** ——
    /// 与"超速触发伺服保护"一致。T 0715 弯曲的试验速率 50mm/min 正好等于本上限，属合法值。
    /// </remarks>
    public const double MaxMillimetersPerMinute = 50.0;

    /// <summary>1 mm/min 对应多少个「速度字段」单位（现役值 10 → 一个单位 = 0.1 mm/min）。</summary>
    public const double UnitsPerMillimeterPerMinute = 10.0;

    /// <summary>
    /// 速率(mm/min) → C4/C5 速度字段值（Uint16）。
    /// 输入先按 <see cref="MaxMillimetersPerMinute"/> 截断（≤0 记 0），再乘
    /// <see cref="UnitsPerMillimeterPerMinute"/>；字段本身还有 Uint16 上限。
    /// </summary>
    public static ushort ToWire(double millimetersPerMinute)
    {
        if (millimetersPerMinute <= 0)
        {
            return 0;
        }

        var clamped = Math.Min(millimetersPerMinute, MaxMillimetersPerMinute);
        var value = Math.Round(clamped * UnitsPerMillimeterPerMinute);
        return value >= ushort.MaxValue ? ushort.MaxValue : (ushort)value;
    }

    /// <summary>速度字段值 → 速率(mm/min)（用于显示 / 核对 / 标定）。</summary>
    public static double FromWire(ushort wireValue) => wireValue / UnitsPerMillimeterPerMinute;

}
