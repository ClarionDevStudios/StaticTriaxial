namespace ANX.Method.Marshall;

using ANX.Core;

/// <summary>
/// 单件试件的峰型判定结果。字段与现役 `TMarshallResult`（`DAO\AsphaltMarshallStabilityDAO.pas`）
/// 中与判定相关的部分一一对应；试验单/试样标识、ChartData、TestStatus 等由上层装配时补。
///
/// 数值口径照现役 `Form\MarshallStabilityTest\MarshallAnalyzer.pas`（JTG 3410-2025 T 0709）：
/// 稳定度两位小数（kN）；修正原点两位小数（下限 0）。
///
/// **流值与马歇尔模数是两位小数**（用户 2026-10-16 定的口径：取值 / 存储 / 显示三处一致）——
/// 现役这两个量取的是一位小数（`SimpleRoundTo(…, -1)`），这是与现役的**有意差异**（已记进知识库）。
/// </summary>
public sealed record MarshallAnalysisResult
{
    /// <summary>峰值是否明显：true = 6.1 明显峰（取曲线最大荷载）；false = 6.2 不明显峰（1.5mm 偏移线交点）。</summary>
    public bool IsObviousPeak { get; init; }

    /// <summary>修正直线拟合（现役 `FindTangentLine`）是否成功。失败时其余数值全 0，与现役返回空记录一致。</summary>
    public bool Succeeded { get; init; }

    /// <summary>修正直线斜率 k。</summary>
    public double TangentK { get; init; }

    /// <summary>修正直线在荷载轴上的截距 b。</summary>
    public double TangentB { get; init; }

    /// <summary>修正原点 O′（mm，两位小数，负值钳 0）。</summary>
    public double OPrime { get; init; }

    /// <summary>目标点 X：明显峰为峰值变形；不明显峰为 1.5mm 偏移线交点变形（mm，两位小数）。</summary>
    public double PeakX { get; init; }

    /// <summary>目标点 Y（kN，两位小数）。</summary>
    public double PeakY { get; init; }

    /// <summary>未修正稳定度 MS（kN，两位小数）。</summary>
    public double MSRaw { get; init; }

    /// <summary>流值 FL = PeakX − O′（mm，**两位小数**）。</summary>
    public double FLValue { get; init; }

    /// <summary>
    /// 不明显峰未找到 1.5mm 偏移线交点、兜底取了曲线末点 —— 现役只打调试日志的那条路径，
    /// C# 侧把它显式带出来，便于上层记日志/提示。
    /// </summary>
    public bool UsedFallbackLastPoint { get; init; }

    internal static MarshallAnalysisResult Failed(bool isObviousPeak) =>
        new() { IsObviousPeak = isObviousPeak };
}

/// <summary>
/// T 0709 马歇尔稳定度判定（逐行照现役 `Form\MarshallStabilityTest\MarshallAnalyzer.pas` 的
/// `CalcObviousPeak` / `CalcNonObviousPeak` / `FindTangentLine` 三个函数搬，含"半界值向零截断"的
/// `SimpleRoundTo` 修约口径 —— 见 <see cref="SimpleRoundTo"/>）。
///
/// 与现役的对应关系：
/// <list type="bullet">
///   <item>明显峰（6.1）：曲线最大荷载即稳定度 MS，目标点取最大荷载点；</item>
///   <item>不明显峰（6.2，按压力/位移阈值停机时）：把修正直线右移 1.5mm，曲线从上方穿越到下方的
///         交点作为目标点，MS 取交点荷载；</item>
///   <item>修正直线（两者共用）：峰值前、荷载落在峰值 10%~80% 区间内的点做最小二乘拟合，
///         3σ 剔除离群点后再拟合一次；修正原点 O′ = −b/k（负值钳 0）。</item>
/// </list>
///
/// **有意的差异（1 处）**：现役 `FindTangentLine` 在 3σ 重拟合出非正斜率时只更新 k、不更新 b
/// （疑似笔误），本类原样保留该行为以逐位对齐，见 <see cref="TryFindTangentLine"/> 内注释。
///
/// 修约的精度差异（已知）：现役 `SimpleRoundTo` 的中间量走 Extended（80 位），C# 只有 double
/// （64 位）；极端"半界值"可能差一个最小修约单位（0.01/0.1），待真机对拍时复核。
///
/// 纯函数、无副作用：同一条曲线进、同样的结果出，便于用真机抓的曲线做黄金用例。
/// </summary>
public static class MarshallAnalyzer
{
    /// <summary>修正直线拟合区间的默认起点（峰值荷载百分比）——现役默认值。</summary>
    public const double FallbackStartPercent = 10.0;

    /// <summary>修正直线拟合区间的默认终点（峰值荷载百分比）——现役默认值。</summary>
    public const double FallbackEndPercent = 80.0;

    /// <summary>不明显峰判定用的偏移线右移量（mm）——现役写死的 1.5。</summary>
    public const double FlowOffsetMM = 1.5;

    /// <summary>马歇尔模量计算的流值下限（现役 `FLValue &gt; 0.01` 才计算，否则记 0）。</summary>
    public const double ModulusFlowGuardMM = 0.01;

    /// <summary>疑似无效判定：曲线最大荷载落在最后 3 个点内（未出现下降沿）。</summary>
    public static bool IsCurvePeakAtEnd(IReadOnlyList<CurvePoint> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Count < 3)
        {
            return false;
        }

        var maxY = data[0].Y;
        var maxIndex = 0;
        for (var i = 1; i < data.Count; i++)
        {
            if (data[i].Y > maxY)
            {
                maxY = data[i].Y;
                maxIndex = i;
            }
        }

        // 峰值落在最后 3 个点内 → 荷载仍在上升 / 未出现下降沿（现役 LMaxIdx >= High - 2）
        return maxIndex >= data.Count - 3;
    }

    /// <summary>
    /// 6.1 峰值明显：稳定度取曲线最大荷载，流值 = 峰值变形 − 修正原点。
    /// </summary>
    /// <param name="data">曲线点（X = 变形 mm，Y = 荷载 kN）。明显峰按现役传"破碎点之前的纯曲线"。</param>
    /// <param name="startPercent">修正直线拟合区间起点（峰值荷载 %，默认 10）。</param>
    /// <param name="endPercent">修正直线拟合区间终点（峰值荷载 %，默认 80）。</param>
    public static MarshallAnalysisResult CalcObviousPeak(
        IReadOnlyList<CurvePoint> data,
        double startPercent = FallbackStartPercent,
        double endPercent = FallbackEndPercent
    )
    {
        ArgumentNullException.ThrowIfNull(data);

        const bool isObvious = true;
        if (!TryFindTangentLine(data, out var k, out var b, startPercent, endPercent))
        {
            return MarshallAnalysisResult.Failed(isObvious);
        }

        var oPrime = SimpleRoundTo(-b / k, -2);
        if (oPrime < 0.0)
        {
            oPrime = 0.0;
        }

        // 最大荷载：按原始顺序扫，取第一个最大值（现役 maxY 从 -1 起、严格大于才替换）
        var maxY = -1.0;
        var maxX = 0.0;
        foreach (var point in data)
        {
            if (point.Y > maxY)
            {
                maxY = point.Y;
                maxX = point.X;
            }
        }

        return new MarshallAnalysisResult
        {
            IsObviousPeak = isObvious,
            Succeeded = true,
            TangentK = k,
            TangentB = b,
            OPrime = oPrime,
            PeakX = SimpleRoundTo(maxX, -2),
            PeakY = SimpleRoundTo(maxY, -2),
            MSRaw = SimpleRoundTo(maxY, -2),
            FLValue = SimpleRoundTo(maxX - oPrime, -2),
        };
    }

    /// <summary>
    /// 6.2 峰值不明显：修正直线右移 1.5mm，取曲线从上方穿越到下方的交点为目标点；
    /// 找不到交点时兜底取曲线末点（现役同款）。按现役传"整条原始曲线"。
    /// </summary>
    /// <param name="data">曲线点（X = 变形 mm，Y = 荷载 kN）。</param>
    /// <param name="startPercent">修正直线拟合区间起点（峰值荷载 %，默认 10）。</param>
    /// <param name="endPercent">修正直线拟合区间终点（峰值荷载 %，默认 80）。</param>
    public static MarshallAnalysisResult CalcNonObviousPeak(
        IReadOnlyList<CurvePoint> data,
        double startPercent = FallbackStartPercent,
        double endPercent = FallbackEndPercent
    )
    {
        ArgumentNullException.ThrowIfNull(data);

        const bool isObvious = false;
        if (!TryFindTangentLine(data, out var k, out var b, startPercent, endPercent))
        {
            return MarshallAnalysisResult.Failed(isObvious);
        }

        var oPrime = SimpleRoundTo(-b / k, -2);
        if (oPrime < 0.0)
        {
            oPrime = 0.0;
        }

        // 右移 1.5mm 的偏移直线：y = k*(x − 1.5) + b = k*x + (b − 1.5k)
        var offsetB = b - (FlowOffsetMM * k);

        // 现役先按 X 升序稳定排序再找交点（保证相邻点按 X 排列）
        var sorted = SortByXStable(data);

        // 兜底：未找到交点时取最大 X 点（排序后即末点）
        var peakX = SimpleRoundTo(sorted[^1].X, -2);
        var peakY = SimpleRoundTo(sorted[^1].Y, -2);
        var found = false;

        for (var i = 1; i < sorted.Length; i++)
        {
            var prev = sorted[i - 1];
            var cur = sorted[i];

            // 上一个点在偏移线上方、当前点落在偏移线下方（含线上）→ 出现交点
            if (cur.Y <= (k * cur.X) + offsetB && prev.Y > (k * prev.X) + offsetB)
            {
                // 相邻点 X 相同（重复点）时斜率无定义：现役 Continue 跳过该段，继续往后找
                if (cur.X == prev.X)
                {
                    continue;
                }

                var segmentSlope = (cur.Y - prev.Y) / (cur.X - prev.X);
                if (Math.Abs(segmentSlope - k) > 1e-9)
                {
                    // 线性插值求两线段准确交点；PeakY 用已修约的 PeakX 回代（现役同款）
                    peakX = SimpleRoundTo(
                        (prev.Y - (segmentSlope * prev.X) - offsetB) / (k - segmentSlope),
                        -2
                    );
                    peakY = SimpleRoundTo((k * peakX) + offsetB, -2);
                    found = true;
                }

                break;
            }
        }

        return new MarshallAnalysisResult
        {
            IsObviousPeak = isObvious,
            Succeeded = true,
            TangentK = k,
            TangentB = b,
            OPrime = oPrime,
            PeakX = peakX,
            PeakY = peakY,
            MSRaw = SimpleRoundTo(peakY, -2),
            FLValue = SimpleRoundTo(peakX - oPrime, -2),
            UsedFallbackLastPoint = !found,
        };
    }

    /// <summary>
    /// 修正直线拟合（照现役 `FindTangentLine` 逐行搬）：按 X 升序排序 → 找曲线峰值 →
    /// 收集峰值之前、荷载落在 <paramref name="startPercent"/>~<paramref name="endPercent"/>（峰值荷载 %）
    /// 区间内的点 → 最小二乘拟合 y = kx + b → 3σ 剔除离群点后重拟合一次。
    /// </summary>
    /// <returns>拟合成功回 true；点数不足、峰值为非正、区间内点不足、斜率为非正等情形回 false（现役同款）。</returns>
    public static bool TryFindTangentLine(
        IReadOnlyList<CurvePoint> data,
        out double k,
        out double b,
        double startPercent = FallbackStartPercent,
        double endPercent = FallbackEndPercent
    )
    {
        ArgumentNullException.ThrowIfNull(data);

        k = 0.0;
        b = 0.0;

        var count = data.Count;
        if (count < 3)
        {
            return false;
        }

        // 参数非法时回退到默认 10%~80%（现役同款校验）
        if (startPercent < 0.0 || endPercent > 100.0 || startPercent >= endPercent)
        {
            startPercent = FallbackStartPercent;
            endPercent = FallbackEndPercent;
        }

        // 1. 按 X 升序稳定排序（现役为稳定插入排序；C# 侧换 O(n log n) 的稳定排序，结果一致）
        var sorted = SortByXStable(data);

        // 2. 找峰值（取第一个最大值），峰值非正直接判失败
        var peakY = sorted[0].Y;
        var peakIndex = 0;
        for (var i = 1; i < count; i++)
        {
            if (sorted[i].Y > peakY)
            {
                peakY = sorted[i].Y;
                peakIndex = i;
            }
        }

        if (peakY <= 0.0)
        {
            return false;
        }

        var yLow = peakY * startPercent / 100.0;
        var yHigh = peakY * endPercent / 100.0;

        // 3. 收集峰值之前且荷载在区间内的点
        var fitPoints = new List<CurvePoint>(peakIndex + 1);
        for (var i = 0; i <= peakIndex; i++)
        {
            if (sorted[i].Y >= yLow && sorted[i].Y <= yHigh)
            {
                fitPoints.Add(sorted[i]);
            }
        }

        if (fitPoints.Count < 3)
        {
            return false;
        }

        // 4. 最小二乘拟合（累加顺序与现役一致，保证浮点结果一致）
        if (!TryLeastSquares(fitPoints, out var slope, out var intercept))
        {
            return false;
        }

        if (slope <= 0.0)
        {
            return false;
        }

        // 5. 剔除 3σ 离群点后重拟合一次（抗单帧毛刺/锤击瞬态）
        var residualSum = 0.0;
        var residualSum2 = 0.0;
        foreach (var point in fitPoints)
        {
            var residual = point.Y - ((slope * point.X) + intercept);
            residualSum += residual;
            residualSum2 += residual * residual;
        }

        var n = fitPoints.Count;
        var stdDev = Math.Sqrt(Math.Max(0.0, (residualSum2 - (residualSum * residualSum / n)) / n));
        if (stdDev > 0.0)
        {
            var kept = new List<CurvePoint>(fitPoints.Count);
            foreach (var point in fitPoints)
            {
                var residual = point.Y - ((slope * point.X) + intercept);
                if (Math.Abs(residual) <= 3.0 * stdDev)
                {
                    kept.Add(point);
                }
            }

            if (kept.Count >= 3 && TryLeastSquares(kept, out var refitSlope, out var refitIntercept))
            {
                // 现役原样保留：重拟合斜率 ≤ 0 时只更新 k、不更新 b（见类注释"有意的差异"）
                slope = refitSlope;
                if (refitSlope > 0.0)
                {
                    intercept = refitIntercept;
                }
            }
        }

        k = slope;
        b = intercept;
        return true;
    }

    /// <summary>
    /// 修正后稳定度 MS′ = MS × K（两位小数）。K 由 <see cref="MarshallCorrectionFactor"/> 按试件高度取。
    /// 现役只在"芯样马歇尔"（`TestSpecificationsID = 1`）分支做这一步，试验单口径由上层的执行层决定。
    /// </summary>
    public static double CorrectedStability(double msRawKN, double kFactor) =>
        SimpleRoundTo(msRawKN * kFactor, -2);

    /// <summary>
    /// 马歇尔模量 T = MS / FL（**两位小数**，kN/mm；与流值同为用户 2026-10-16 定的口径）。
    /// 流值不大于 0.01mm 时不计算，记 0（现役同款）。
    /// </summary>
    /// <param name="stabilityKN">明显峰用 MSRaw、芯样分支用修正后的 MS。</param>
    public static double MarshallModulus(double stabilityKN, double flowValueMM) =>
        flowValueMM > ModulusFlowGuardMM ? SimpleRoundTo(stabilityKN / flowValueMM, -2) : 0.0;

    /// <summary>
    /// 现役 `SimpleRoundTo(值, digit)` 的移植：digit 为负表示小数位数（−2 → 两位）。
    /// 语义是"半界值远离零"，但实现为"除以 10^digit 后 ±0.5、向零截断、再乘回"，
    /// 与数学上的四舍五入在二进制无法精确表示 0.1/0.01 时会有差别（如 2.05 → 2.0）——
    /// **这个行为与现役一致**，不要换成 `Math.Round`。
    /// </summary>
    public static double SimpleRoundTo(double value, int digit)
    {
        var factor = PowerOfTen(digit);
        return value < 0
            ? Math.Truncate((value / factor) - 0.5) * factor
            : Math.Truncate((value / factor) + 0.5) * factor;
    }

    /// <summary>按 X 升序稳定排序（与现役的手写插入排序结果一致；LINQ OrderBy 是稳定排序）。</summary>
    private static CurvePoint[] SortByXStable(IReadOnlyList<CurvePoint> data) =>
        [.. data.OrderBy(point => point.X)];

    /// <summary>
    /// 最小二乘拟合 y = kx + b（累加顺序照现役）：横坐标全相同（分母 ≤ 0）时判失败。
    /// </summary>
    private static bool TryLeastSquares(
        IReadOnlyList<CurvePoint> points,
        out double slope,
        out double intercept
    )
    {
        slope = 0.0;
        intercept = 0.0;

        var sumX = 0.0;
        var sumY = 0.0;
        var sumXX = 0.0;
        var sumXY = 0.0;
        foreach (var point in points)
        {
            sumX += point.X;
            sumY += point.Y;
            sumXX += point.X * point.X;
            sumXY += point.X * point.Y;
        }

        var count = points.Count;
        var meanX = sumX / count;
        var meanY = sumY / count;
        var denominator = sumXX - (count * meanX * meanX);
        if (denominator <= 0.0)
        {
            return false;
        }

        slope = (sumXY - (count * meanX * meanY)) / denominator;
        intercept = meanY - (slope * meanX);
        return true;
    }

    /// <summary>10^exponent 的整数次幂（照现役 `IntPower(10, digit)` 的快速幂算法，保证与现役同值）。</summary>
    private static double PowerOfTen(int exponent)
    {
        var y = Math.Abs(exponent);
        var baseValue = 10.0;
        var result = 1.0;
        while (y > 0)
        {
            while ((y & 1) == 0)
            {
                y >>= 1;
                baseValue *= baseValue;
            }

            y--;
            result *= baseValue;
        }

        return exponent < 0 ? 1.0 / result : result;
    }
}
