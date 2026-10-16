namespace ANX.Method.Marshall;

/// <summary>试件尺寸档：φ101.6×63.5（小）与 φ152.4×95.3（大）。与现役 `TSpecimentSize` 对应。</summary>
public enum MarshallSpecimenSize
{
    /// <summary>φ100mm 档（现役 `IsSmallSpeciment`，标准高度 63.5mm）。</summary>
    Small100mm = 0,

    /// <summary>φ150mm 档（现役 `IsLargeSpeciment`，标准高度 95.2mm）。</summary>
    Large150mm = 1,
}

/// <summary>高度超出表范围时的处理方式（与现役三个常量的取值一一对应）。</summary>
public enum MarshallOutOfBoundsPolicy
{
    /// <summary>拒绝计算（现役 `OOB_RETURN_FALSE`）。</summary>
    Reject = 0,

    /// <summary>截断到端点系数（现役 `OOB_CLAMP`，**现役默认**）。</summary>
    Clamp = 1,

    /// <summary>按边缘两点做线性外推（现役 `OOB_EXTRAPOLATE`）。</summary>
    Extrapolate = 2,
}

/// <summary>
/// 马歇尔稳定度**试件高度修正系数 K**：按试件高度查表 + 线性插值（照现役
/// `Common\MarshallCorrectionFactor.pas` 逐行搬，含默认的"越界截断"策略与**两位小数的取整口径**）。
///
/// 口径要点（都与现役一致）：
/// <list type="bullet">
///   <item>两张表：φ100mm 档 33 个点（25.4~76.2mm，标准高度 63.5 → K=1.00）、
///         φ150mm 档 9 个点（88.9~101.6mm，标准高度 95.2 → K=1.00）；</item>
///   <item>命中表格点用原值，落在两点之间做**线性插值**；系数一律 `Round(…, 2)`
///         （现役 `SimpleRoundTo(K, -2)`）；</item>
///   <item>数据出自《公路工程沥青及沥青混合料试验规程》JTG E20-2011 表 T 0710-1 / T 0710-2。</item>
/// </list>
/// </summary>
public static class MarshallCorrectionFactor
{
    /// <summary>φ100mm 档的标准高度（mm），此高度 K = 1.00。</summary>
    public const double StandardHeightSmallMM = 63.5;

    /// <summary>φ150mm 档的标准高度（mm），此高度 K = 1.00。</summary>
    public const double StandardHeightLargeMM = 95.2;

    /// <summary>表 T 0710-1：φ100mm 试件高度（mm）→ 修正系数 K。</summary>
    private static readonly (double Height, double Factor)[] Table100 =
    [
        (25.4, 5.56), (27.0, 5.00), (28.6, 4.55), (30.2, 4.17), (31.8, 3.85),
        (33.3, 3.57), (34.9, 3.33), (36.5, 3.03), (38.1, 2.78), (39.7, 2.50),
        (41.3, 2.27), (42.9, 2.08), (44.4, 1.92), (46.0, 1.79), (47.6, 1.67),
        (49.2, 1.56), (50.8, 1.47), (52.4, 1.39), (54.0, 1.32), (55.6, 1.25),
        (57.2, 1.19), (58.7, 1.14), (60.3, 1.09), (61.9, 1.04), (63.5, 1.00),
        (65.1, 0.96), (66.7, 0.93), (68.3, 0.89), (69.8, 0.86), (71.4, 0.83),
        (73.0, 0.81), (74.6, 0.78), (76.2, 0.76),
    ];

    /// <summary>表 T 0710-2：φ150mm 试件高度（mm）→ 修正系数 K。</summary>
    private static readonly (double Height, double Factor)[] Table150 =
    [
        (88.9, 1.12), (90.5, 1.09), (92.1, 1.06), (93.7, 1.03), (95.2, 1.00),
        (96.8, 0.97), (98.4, 0.95), (100.0, 0.92), (101.6, 0.90),
    ];

    /// <summary>φ100mm 档的表格点数（与现役 `MARSHALL_100_SIZE` 一致）。</summary>
    public static int Table100Size => Table100.Length;

    /// <summary>φ150mm 档的表格点数（与现役 `MARSHALL_150_SIZE` 一致）。</summary>
    public static int Table150Size => Table150.Length;

    /// <summary>
    /// 按试件高度取修正系数。<paramref name="policy"/> 默认"越界截断"（与现役默认值 1 一致）。
    /// </summary>
    /// <returns>取到回 <c>true</c>；<see cref="MarshallOutOfBoundsPolicy.Reject"/> 且越界时回 <c>false</c>。</returns>
    public static bool TryGet(
        double heightMM,
        MarshallSpecimenSize size,
        out double factor,
        MarshallOutOfBoundsPolicy policy = MarshallOutOfBoundsPolicy.Clamp
    )
    {
        var table = size == MarshallSpecimenSize.Small100mm ? Table100 : Table150;

        return TryGet(heightMM, table, out factor, policy);
    }

    private static bool TryGet(
        double heightMM,
        (double Height, double Factor)[] table,
        out double factor,
        MarshallOutOfBoundsPolicy policy
    )
    {
        factor = 1.0;

        var (minHeight, minFactor) = table[0];
        var (maxHeight, maxFactor) = table[^1];

        if (heightMM < minHeight)
        {
            return ApplyOutOfBounds(heightMM, table[0], table[1], table[0], policy, out factor);
        }

        if (heightMM > maxHeight)
        {
            return ApplyOutOfBounds(
                heightMM,
                table[^2],
                table[^1],
                table[^1],
                policy,
                out factor
            );
        }

        // 表内：命中表格点用原值，落在两点之间线性插值（现役同样逐个区间扫）
        for (var i = 0; i < table.Length - 1; i++)
        {
            var (lowHeight, lowFactor) = table[i];
            var (highHeight, highFactor) = table[i + 1];

            if (heightMM <= highHeight)
            {
                factor = Round(lowFactor + ((heightMM - lowHeight) / (highHeight - lowHeight)) * (highFactor - lowFactor));
                return true;
            }
        }

        factor = Round(maxFactor);
        return true;
    }

    /// <summary>越界处理：现役三种策略。截断用端点系数、外推用边缘两点（都要 Round 到两位）。</summary>
    private static bool ApplyOutOfBounds(
        double heightMM,
        (double Height, double Factor) near,
        (double Height, double Factor) far,
        (double Height, double Factor) clampTo,
        MarshallOutOfBoundsPolicy policy,
        out double factor
    )
    {
        switch (policy)
        {
            case MarshallOutOfBoundsPolicy.Clamp:
                factor = Round(clampTo.Factor);
                return true;

            case MarshallOutOfBoundsPolicy.Extrapolate:
                var slope = (far.Factor - near.Factor) / (far.Height - near.Height);
                factor = Round(near.Factor + ((heightMM - near.Height) * slope));
                return true;

            default:
                factor = 1.0;
                return false;
        }
    }

    /// <summary>系数取整到两位小数 —— 与现役 `SimpleRoundTo(K, -2)` 同口径。</summary>
    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
