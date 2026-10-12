namespace ANX.Method.Bending;

using ANX.Core;

/// <summary>单件试件的判定结果。各字段的精度按规范要求（强度 0.01MPa、挠度 0.1mm、应变 0.1με）。</summary>
public sealed record BendingSampleResult(
    double PeakLoadKN,
    double PeakDeflectionMM,
    double CorrectedDeflectionMM,
    double FlexuralStrengthMPa,
    double MaxBendingTensileStrainMicro,
    double TangentK,
    double TangentB,
    double OriginCorrectionMM
);

/// <summary>
/// 一组试件的统计结果（规范 5.5 / 5.6）。
/// </summary>
/// <param name="EffectiveSamples">参与统计的有效试件（已剔除超差者）。</param>
/// <param name="ExcludedIndexes">被剔除试件在入参里的下标。</param>
/// <param name="MeanFlexuralStrengthMPa">弯拉强度算术平均值（0.01MPa）。</param>
/// <param name="MeanMaxBendingTensileStrainMicro">最大弯拉应变算术平均值（1με）。</param>
/// <param name="EffectiveCount">有效试件数。</param>
/// <param name="NeedsSupplement">有效试件不足 3 个，应补充试验。</param>
public sealed record BendingGroupResult(
    IReadOnlyList<BendingSampleResult> EffectiveSamples,
    IReadOnlyList<int> ExcludedIndexes,
    double MeanFlexuralStrengthMPa,
    double MeanMaxBendingTensileStrainMicro,
    int EffectiveCount,
    bool NeedsSupplement
);

/// <summary>
/// T 0715-2025 沥青混合料弯曲试验判定（棱柱体小梁三点弯曲）。
///
/// 规范依据（JTG 3410-2025 T 0715-2025）：
///   5.1 绘制荷载-跨中挠度曲线并进行**原点修正**；
///   5.2 由修正后曲线确定破坏荷载 P_B 与跨中挠度 d；
///   5.3 R_B = 3·L·P_B / (2·b·h²)，精确至 0.01MPa（P_B 以 N 计）；
///   5.4 ε_B = 6·h·d / L²，精确至 0.1（με）；
///   5.5 一组取算术平均值；
///   5.5 一组取算术平均值；5.6 允许误差不满足时剔除与平均值偏差最大的测定值后重算。
///
/// 纯函数、无副作用：同一条曲线进、同样的结果出，便于用真机抓的曲线做黄金用例。
/// </summary>
public static class BendingAnalyzer
{
    /// <summary>弯曲强度的报告精度（MPa）。</summary>
    public const int StrengthDecimals = 2;

    /// <summary>挠度的报告精度（mm）。</summary>
    public const int DeflectionDecimals = 1;

    /// <summary>单片试件弯曲应变的报告精度（με）—— 规范 5.4 要求 0.1。</summary>
    public const int StrainDecimals = 1;

    /// <summary>一组弯曲应变平均值的报告精度（με）—— 规范 5.5 要求 1。组统计在 M3 第 3 步落地时按它舍入。</summary>
    public const int GroupMeanStrainDecimals = 0;

    /// <summary>规范 6：最大弯拉应变的重复性试验允许误差（με）。</summary>
    public const double RepeatabilityAllowanceMicro = 400.0;

    /// <summary>规范 5.6：剔除后有效试件少于 3 个时应补充试验。</summary>
    public const int MinimumEffectiveSamples = 3;

    /// <summary>
    /// 修正直线的拟合区间默认值：峰值荷载的 10%~80%。
    /// **规范未规定拟合方法**，这里按现役实现口径取 10%~80%（现役代码默认值；现场可配，见参数表
    /// `OriginCorrectionStart` / `OriginCorrectionEnd`）。
    /// </summary>
    public const double FallbackStartPercent = 10.0;

    public const double FallbackEndPercent = 80.0;

    /// <summary>5.3 弯曲强度 R_B = 3·L·P_B / (2·b·h²)。P_B 以 kN 传入，内部换算成 N。</summary>
    /// <param name="peakLoadKN">破坏荷载 P_B（kN）。</param>
    /// <param name="spanMM">支点间距（跨径）L（mm）。</param>
    /// <param name="widthMM">试件宽度 b（mm）。</param>
    /// <param name="heightMM">试件高度 h（mm）。</param>
    /// <exception cref="ArgumentOutOfRangeException">几何参数非正时抛出（见算法对照表：这是与现役"返回 0"的有意差异）。</exception>
    public static double FlexuralStrength(double peakLoadKN, double spanMM, double widthMM, double heightMM)
    {
        ValidatePositive(spanMM, nameof(spanMM));
        ValidatePositive(widthMM, nameof(widthMM));
        ValidatePositive(heightMM, nameof(heightMM));

        var strength = 3.0 * spanMM * (peakLoadKN * 1000.0) / (2.0 * widthMM * heightMM * heightMM);
        return RoundTo(strength, StrengthDecimals);
    }

    /// <summary>5.4 最大弯拉应变 ε_B = 6·h·d / L²，结果以 με（10⁻⁶）计。</summary>
    public static double MaxBendingTensileStrain(double heightMM, double deflectionMM, double spanMM)
    {
        ValidatePositive(heightMM, nameof(heightMM));
        ValidatePositive(spanMM, nameof(spanMM));

        var strain = 6.0 * heightMM * deflectionMM / (spanMM * spanMM) * 1e6;
        return RoundTo(strain, StrainDecimals);
    }

    /// <summary>
    /// 按规范 5.1/5.2 对一条曲线做判定：先做原点修正，再取曲线最大荷载作为破坏荷载 P_B。
    /// </summary>
    /// <param name="curve">荷载-跨中挠度曲线（X = 挠度 mm，Y = 荷载 kN）。</param>
    /// <param name="spanMM">跨径 L。</param>
    /// <param name="widthMM">试件宽度 b。</param>
    /// <param name="heightMM">试件高度 h。</param>
    /// <param name="enableOriginCorrection">
    /// 是否做曲线原点修正（参数表 `EnableOriginCorrection`）。**有的试验需要、有的不需要**（用户 2026-10-12）：
    /// 关闭时不拟合修正直线，修正原点记 0，校正挠度 = 峰值挠度（应变随之按未修正挠度算）。
    /// </param>
    public static BendingSampleResult Analyze(
        IReadOnlyList<CurvePoint> curve,
        double spanMM,
        double widthMM,
        double heightMM,
        bool enableOriginCorrection = true,
        double originCorrectionStartPercent = FallbackStartPercent,
        double originCorrectionEndPercent = FallbackEndPercent,
        bool lockPeakDisplacement = true
    )
    {
        ArgumentNullException.ThrowIfNull(curve);
        if (curve.Count < 3)
        {
            throw new ArgumentException("曲线点数不足以做原点修正（至少 3 点）", nameof(curve));
        }

        var (peakLoad, peakDeflection) = FindMaxLoadPoint(curve);

        // 5.1 修正原点 O′ = −b / k（修正直线与横轴交点）；为负时钳到 0（现役口径）
        var k = 0.0;
        var b = 0.0;
        var originCorrection = 0.0;

        if (enableOriginCorrection)
        {
            var tangent = FitTangentLine(
                curve,
                originCorrectionStartPercent,
                originCorrectionEndPercent
            );
            if (tangent is null)
            {
                throw new InvalidOperationException("无法拟合修正直线（峰值非正或上升段不足）");
            }

            (k, b) = tangent.Value;
            originCorrection = Math.Max(0.0, -b / k);
        }

        // 锁定峰值位移（现役 LockDisplacement，**每个试验都需要的默认选中项**）：
        //   开（默认）= 跨中挠度取**峰值荷载点**的位移；
        //   关       = 跨中挠度取曲线里**最大位移**（破坏荷载仍取峰值荷载点）。
        var reportedDeflection = lockPeakDisplacement
            ? peakDeflection
            : curve.Max(point => point.X);

        var correctedDeflection = Math.Max(0.0, reportedDeflection - originCorrection);

        return new BendingSampleResult(
            RoundTo(peakLoad, StrengthDecimals),
            RoundTo(reportedDeflection, StrengthDecimals),
            RoundTo(correctedDeflection, DeflectionDecimals),
            FlexuralStrength(peakLoad, spanMM, widthMM, heightMM),
            MaxBendingTensileStrain(heightMM, correctedDeflection, spanMM),
            k,
            b,
            RoundTo(originCorrection, StrengthDecimals)
        );
    }

    /// <summary>
    /// 按规范精度舍入（四舍五入、远离零）。
    /// **为什么要多一步**：结果常常恰好落在舍入中点上（例如交点算出来是 4.75mm），
    /// 而二进制浮点会把 4.75 算成 4.749999999999999，直接舍入就变成 4.7。
    /// 这里先按 6 位小数把浮点噪声吃掉，再按目标精度舍入。
    /// </summary>
    public static double RoundTo(double value, int decimals)
    {
        var scale = Math.Pow(10, decimals);
        var scaled = Math.Round(value * scale, 6, MidpointRounding.AwayFromZero);
        return Math.Round(scaled, MidpointRounding.AwayFromZero) / scale;
    }

    /// <summary>
    /// 5.1 修正直线：按 X 升序 → 只取峰值前的上升段 → 居中移动平均平滑（仅用于拟合）→

    /// <summary>
    /// 规范 5.5 / 5.6 与 6：一组试件的算术平均值，以及"最大弯拉应变超差则剔除偏差最大者后重算"。
    /// 剔除到有效试件少于 <see cref="MinimumEffectiveSamples"/> 个时不再剔除，并置
    /// <see cref="BendingGroupResult.NeedsSupplement"/> 提示补充试验。
    /// </summary>
    public static BendingGroupResult AnalyzeGroup(IReadOnlyList<BendingSampleResult> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            throw new ArgumentException("一组至少要有 1 个试件", nameof(samples));
        }

        var working = samples.Select((sample, index) => (Index: index, Sample: sample)).ToList();
        var excluded = new List<int>();

        while (working.Count > 1)
        {
            var meanStrain = working.Average(w => w.Sample.MaxBendingTensileStrainMicro);
            var worst = working
                .OrderByDescending(w => Math.Abs(w.Sample.MaxBendingTensileStrainMicro - meanStrain))
                .First();

            if (Math.Abs(worst.Sample.MaxBendingTensileStrainMicro - meanStrain) <= RepeatabilityAllowanceMicro)
            {
                break;
            }

            if (working.Count - 1 < MinimumEffectiveSamples)
            {
                break;
            }

            excluded.Add(worst.Index);
            working.Remove(worst);
        }

        var effective = working.Select(w => w.Sample).ToArray();

        return new BendingGroupResult(
            effective,
            excluded,
            RoundTo(effective.Average(s => s.FlexuralStrengthMPa), StrengthDecimals),
            RoundTo(effective.Average(s => s.MaxBendingTensileStrainMicro), GroupMeanStrainDecimals),
            effective.Length,
            effective.Length < MinimumEffectiveSamples
        );
    }

    /// <summary>
    /// 5.1 修正直线：按 X 升序 → 只取峰值前的上升段 → 居中移动平均平滑（仅用于拟合）→
    /// 滑窗找 R² 最高的直线段（不足则回退到峰值 10%~80% 区间）→ 最小二乘 → 3σ 剔除重拟合。
    /// </summary>
    /// <returns>拟合成功的 (k, b)；失败返回 null。</returns>
    public static (double K, double B)? FitTangentLine(
        IReadOnlyList<CurvePoint> curve,
        double startPercent = FallbackStartPercent,
        double endPercent = FallbackEndPercent
    )
    {
        const int smoothWindow = 5;
        const int minFitPoints = 12;
        const double minFitR2 = 0.99;
        const double minLoadSpanRatio = 0.20;
        const double maxSpanRatio = 0.60;

        var sorted = curve.OrderBy(p => p.X).ToArray();
        var peakIndex = 0;
        for (var i = 1; i < sorted.Length; i++)
        {
            if (sorted[i].Y > sorted[peakIndex].Y)
            {
                peakIndex = i;
            }
        }

        var peakY = sorted[peakIndex].Y;
        if (peakY <= 0)
        {
            return null;
        }

        var smooth = MovingAverage(sorted, smoothWindow);
        var ascending = smooth[..(peakIndex + 1)];

        // 拟合区间限制（现役把 OriginCorrectionStart/End 当"压力峰值%"传进 FindTangentLine）：
        // 只在峰值荷载的 start%~end% 这一段里找直线段；点数不够就退回整段上升沿
        var low = peakY * Math.Clamp(startPercent, 1.0, 99.0) / 100.0;
        var high = peakY * Math.Clamp(endPercent, 2.0, 100.0) / 100.0;
        if (low >= high)
        {
            low = peakY * FallbackStartPercent / 100.0;
            high = peakY * FallbackEndPercent / 100.0;
        }

        var band = ascending.Where(p => p.Y >= low && p.Y <= high).ToArray();
        if (band.Length >= 3)
        {
            ascending = band;
            peakIndex = ascending.Length - 1; // 区间内的最后一点就是区间上沿
        }

        // 滑窗自动选直线段
        var maxLength = Math.Min(
            ascending.Length,
            Math.Max(minFitPoints, (int)Math.Round((peakIndex + 1) * maxSpanRatio)));

        var bestStart = -1;
        var bestLength = 0;
        var bestR2 = -1.0;

        for (var length = minFitPoints; length <= maxLength;)
        {
            var step = Math.Max(1, length / 4);
            for (var start = 0; start + length <= ascending.Length; start += step)
            {
                if (!TryFitWindow(ascending, start, length, out _, out _, out var r2))
                {
                    continue;
                }

                var loadSpan = ascending[start + length - 1].Y - ascending[start].Y;
                if (loadSpan < peakY * minLoadSpanRatio)
                {
                    continue;
                }

                if (r2 > bestR2 + 0.0005 || (Math.Abs(r2 - bestR2) <= 0.0005 && length > bestLength))
                {
                    bestR2 = r2;
                    bestStart = start;
                    bestLength = length;
                }
            }

            length = length < minFitPoints + 1 ? length + 1 : (int)Math.Round(length * 1.3);
        }

        CurvePoint[] fitPoints;
        if (bestStart >= 0 && bestR2 >= minFitR2)
        {
            fitPoints = ascending.Skip(bestStart).Take(bestLength).ToArray();
        }
        else
        {
            // 回退：就用当前拟合区间内的点（上方已按区间裁过）
            fitPoints = ascending;
        }

        if (fitPoints.Length < 3)
        {
            return null;
        }

        if (!TryFitWindow(fitPoints, 0, fitPoints.Length, out var slope, out var intercept, out _) || slope <= 0)
        {
            return null;
        }

        // 3σ 剔除离群点后重拟合一次（抗单帧毛刺）
        var residuals = fitPoints.Select(p => p.Y - (slope * p.X + intercept)).ToArray();
        var mean = residuals.Average();
        var sigma = Math.Sqrt(residuals.Select(r => (r - mean) * (r - mean)).Sum() / residuals.Length);
        if (sigma > 0)
        {
            var kept = fitPoints.Where((_, i) => Math.Abs(residuals[i]) <= 3.0 * sigma).ToArray();
            if (kept.Length >= 3
                && TryFitWindow(kept, 0, kept.Length, out var k2, out var b2, out _)
                && k2 > 0)
            {
                slope = k2;
                intercept = b2;
            }
        }

        return (slope, intercept);
    }

    private static (double Load, double Deflection) FindMaxLoadPoint(IReadOnlyList<CurvePoint> curve)
    {
        var best = curve[0];
        foreach (var point in curve)
        {
            if (point.Y > best.Y)
            {
                best = point;
            }
        }

        return (best.Y, best.X);
    }

    private static CurvePoint[] MovingAverage(IReadOnlyList<CurvePoint> points, int window)
    {
        var half = window / 2;
        var result = new CurvePoint[points.Count];

        for (var i = 0; i < points.Count; i++)
        {
            var low = Math.Max(0, i - half);
            var high = Math.Min(points.Count - 1, i + half);
            var sum = 0.0;
            for (var j = low; j <= high; j++)
            {
                sum += points[j].Y;
            }

            result[i] = new CurvePoint(points[i].X, sum / (high - low + 1));
        }

        return result;
    }

    private static bool TryFitWindow(
        IReadOnlyList<CurvePoint> points,
        int start,
        int length,
        out double slope,
        out double intercept,
        out double r2
    )
    {
        slope = 0;
        intercept = 0;
        r2 = 0;

        if (length < 2 || start < 0 || start + length > points.Count)
        {
            return false;
        }

        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = start; i < start + length; i++)
        {
            sx += points[i].X;
            sy += points[i].Y;
            sxx += points[i].X * points[i].X;
            sxy += points[i].X * points[i].Y;
        }

        var meanX = sx / length;
        var meanY = sy / length;
        var denominator = sxx - length * meanX * meanX;
        if (denominator <= 0)
        {
            return false;
        }

        slope = (sxy - length * meanX * meanY) / denominator;
        if (slope <= 0)
        {
            return false;
        }

        intercept = meanY - slope * meanX;

        double ssTot = 0, ssRes = 0;
        for (var i = start; i < start + length; i++)
        {
            var predicted = slope * points[i].X + intercept;
            ssTot += (points[i].Y - meanY) * (points[i].Y - meanY);
            ssRes += (points[i].Y - predicted) * (points[i].Y - predicted);
        }

        r2 = ssTot > 0 ? 1 - ssRes / ssTot : 0;
        return true;
    }

    private static void ValidatePositive(double value, string parameterName)
    {
        if (!(value > 0) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "几何参数必须为正的有限值");
        }
    }
}
