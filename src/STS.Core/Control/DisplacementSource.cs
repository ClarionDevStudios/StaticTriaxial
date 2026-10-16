namespace STS.Core.Control;

/// <summary>
/// 轴向位移的**来源**（用户 2026-10-12：双传感器的选择不固定，四种平均方式都可能用）。
/// 数值直接沿用现役 `AxialDisplacementChannel` 的编号，便于两边数据对照：
/// 0~3 位移通道 1~4（单路）；4~7 千分表 1~4（单路）；8~11 四组双通道取平均。
/// </summary>
public enum DisplacementSource
{
    /// <summary>位移通道 1（单路）。</summary>
    Displace1 = 0,

    /// <summary>位移通道 2（单路）—— 目前默认值（真机验证时的读取通道）。</summary>
    Displace2 = 1,

    /// <summary>位移通道 3（单路）。</summary>
    Displace3 = 2,

    /// <summary>位移通道 4（单路）。</summary>
    Displace4 = 3,

    /// <summary>千分表 1（单路；原始值 /1000 = mm，口径同现役）。</summary>
    Micrometer1 = 4,

    /// <summary>千分表 2（单路）。</summary>
    Micrometer2 = 5,

    /// <summary>千分表 3（单路）。</summary>
    Micrometer3 = 6,

    /// <summary>千分表 4（单路）。</summary>
    Micrometer4 = 7,

    /// <summary>位移 1+2 通道平均值。</summary>
    DisplaceAverage12 = 8,

    /// <summary>位移 3+4 通道平均值。</summary>
    DisplaceAverage34 = 9,

    /// <summary>千分表 1+2 通道平均值。</summary>
    MicrometerAverage12 = 10,

    /// <summary>千分表 3+4 通道平均值。</summary>
    MicrometerAverage34 = 11,
}

/// <summary>位移来源的解析与判定（纯逻辑，便于单测）。</summary>
public static class DisplacementSources
{
    /// <summary>把参数表里的选项值（0~11）解析成来源；越界回落到默认（位移通道 2）。</summary>
    public static DisplacementSource Parse(int value) =>
        Enum.IsDefined(typeof(DisplacementSource), value)
            ? (DisplacementSource)value
            : DisplacementSource.Displace2;

    /// <summary>是否两路取平均（双传感器）。</summary>
    public static bool IsAverage(this DisplacementSource source) => (int)source >= 8;

    /// <summary>是否取千分表数据（其余取位移通道数据）。</summary>
    public static bool IsMicrometer(this DisplacementSource source)
    {
        var value = (int)source;
        return (value >= 4 && value <= 7) || value >= 10;
    }

    /// <summary>参与的两路索引（单路来源返回同一索引两次）。</summary>
    public static (int First, int Second) Channels(this DisplacementSource source)
    {
        var value = (int)source;

        return value switch
        {
            >= 0 and <= 3 => (value, value),
            >= 4 and <= 7 => (value - 4, value - 4),
            8 => (0, 1),
            9 => (2, 3),
            10 => (0, 1),
            _ => (2, 3),
        };
    }
}
