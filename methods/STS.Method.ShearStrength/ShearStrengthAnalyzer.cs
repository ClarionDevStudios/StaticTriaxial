namespace STS.Method.ShearStrength;

using STS.Core;

/// <summary>
/// 单件试件的判定结果（T 1236—2026）。
/// </summary>
/// <param name="PeakLoadKN">最大轴向荷载 Pmax（kN；曲线单位就这样，转 N 在内部换算）。</param>
/// <param name="PeakDeformationMM">取 Pmax 时的轴向变形 Δh（mm）。</param>
/// <param name="AxialStrainPercent">破坏时轴向应变 ε1（%；式 T 1236-1）。</param>
/// <param name="CorrectedAreaMM2">校正后试样面积 A（mm²；式 T 1236-2）。</param>
/// <param name="ConfiningPressureKPa">该试件所属围压 σ3（kPa）。</param>
/// <param name="ShearStrengthKPa">试件剪切强度 σ1max（kPa；式 T 1236-3）。</param>
/// <param name="UsedStrain15Rule">是否因轴向变形超过 15% 而采用"变形 15% 对应的轴向荷载"作 Pmax。</param>
public sealed record ShearStrengthSampleResult(
    double PeakLoadKN,
    double PeakDeformationMM,
    double AxialStrainPercent,
    double CorrectedAreaMM2,
    double ConfiningPressureKPa,
    double ShearStrengthKPa,
    bool UsedStrain15Rule
);

/// <summary>一级围压下 3 件试件的统计结果（规范 5.4：算术平均值）。</summary>
public sealed record ShearStrengthLevelResult(
    double ConfiningPressureKPa,
    double MeanShearStrengthKPa,
    int Count
);

/// <summary>莫尔-库仑包络线拟合结果（规范 5.5.1）。</summary>
/// <param name="Slope">回归斜率 m。</param>
/// <param name="Intercept">回归截距 b（kPa）。</param>
/// <param name="FrictionAngleDegrees">内摩擦角 φ（°；φ = arcsin m）。</param>
/// <param name="CohesionKPa">黏聚力 c（kPa；c = b / cosφ）。</param>
/// <param name="Correlation">相关系数 r（规范要求 r ≥ 0.99）。</param>
/// <param name="IsReliable">r ≥ 0.99 且斜率可解（|m| &lt; 1）。</param>
public sealed record EnvelopeFitResult(
    double Slope,
    double Intercept,
    double FrictionAngleDegrees,
    double CohesionKPa,
    double Correlation,
    bool IsReliable
);

/// <summary>抗剪强度比（SSR）计算结果（规范 5.6 / 5.7；"必要时"用）。</summary>
public sealed record SsrResult(
    double ShearPlaneNormalStressKPa,
    double ShearStressOnPlaneKPa,
    double MaxShearStrengthKPa,
    double Ssr
);

/// <summary>
/// T 1236—2026 抗剪强度试验（静三轴法）判定 —— 纯函数，无副作用：同样的曲线进、同样的结果出。
///
/// 规范依据（JTG 3442—2026《公路工程无结合料粒料试验规程》T 1236—2026，用户 2026-10-16 提供的扫描件）：
/// - 5.1 ε1 = Δh / h0 × 100（式 T 1236-1）
/// - 5.2 A  = A0 / (1 − ε1/100)（式 T 1236-2）
/// - 5.3 σ1max = Pmax / A × 10³ + σ3（式 T 1236-3；Pmax 以 N 计、A 以 mm² 计，结果 kPa）
/// - 5.4 每级围压取 3 件剪切强度的**算术平均**（规范未给剔除规则）
/// - 5.5 φ = arcsin m、c = b / cosφ、r（式 T 1236-4/5/6/7/8，最小二乘用莫尔圆圆心 p=(σ1max+σ3)/2 与半径 q=(σ1max−σ3)/2），r ≥ 0.99
/// - 5.6 τmax = c + σ_f·tanφ；σ_f = σ3 + ½(σ1 − σ3)·(1 − √(tan²φ/(1+tan²φ)))（式 T 1236-9/10；根号项恒等于 sinφ）
/// - 5.7 τ_f = √( ((σ1−σ3)/2)² − (σ_f − (σ1+σ3)/2)² )；SSR = τ_f / τmax（式 T 1236-11/12）
///
/// 试验口径（与土工规范的"结构性差异"，见项目卡片）：
/// - 曲线按"**轴向变形（mm）- 轴向荷载（kN）**"记录（与框架 CurvePoint 的 X/Y 一致）；
/// - 停止/破坏判据由控制层按规范 4.6 实现（"每 0.5mm 变形对应轴向压力增加 &lt; 5kPa" 或 "轴向变形超过 15%"），
///   本判定器只提供常量与纯函数配合；
/// - 单件只做一个围压（规范 4.8；不允许一个试件做多级）。
/// </summary>
public static class ShearStrengthAnalyzer
{
    // ── 规范常量（T 1236—2026）────────────────────────────────────────────

    /// <summary>规范 4.8：试验用围压不少于 3 级。</summary>
    public const int MinimumConfiningLevels = 3;

    /// <summary>规范 3.3.1 / 4.8：每个围压水平测试 3 个试件。</summary>
    public const int SpecimensPerLevel = 3;

    /// <summary>规范 4.8：本方法默认围压级别（kPa）。</summary>
    public static readonly IReadOnlyList<double> DefaultConfiningPressuresKPa = [30, 60, 100];

    /// <summary>规范 4.1.3：初始高度（三次 120° 测量均值）合格窗 = 306mm ± 5mm。</summary>
    public const double NominalSpecimenHeightMM = 306.0;

    public const double SpecimenHeightToleranceMM = 5.0;

    /// <summary>规范 4.5：轴向加压应变速率为 1%/min ~ 2%/min。</summary>
    public const double MinStrainRatePercentPerMinute = 1.0;

    public const double MaxStrainRatePercentPerMinute = 2.0;

    /// <summary>规范 4.6：每 0.5mm 变形对应的轴向压力增加小于 5kPa 视作"荷载不再增加"。</summary>
    public const double PlateauStressIncrementKPa = 5.0;

    /// <summary>规范 4.6：轴向变形超过 15% 即停止，采用 15% 变形对应的轴向荷载作 Pmax。</summary>
    public const double MaxStrainStopPercent = 15.0;

    /// <summary>规范 4.3：接触应力 3~5kPa。</summary>
    public const double ContactStressMinKPa = 3.0;

    public const double ContactStressMaxKPa = 5.0;

    /// <summary>规范 5.5.1：相关系数 r 应不小于 0.99。</summary>
    public const double MinimumCorrelation = 0.99;

    // ── 单件计算 ──────────────────────────────────────────────────────────

    /// <summary>轴向应变 ε1（%；式 T 1236-1）。</summary>
    public static double AxialStrainPercent(double deformationMM, double initialHeightMM)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialHeightMM);

        return deformationMM / initialHeightMM * 100.0;
    }

    /// <summary>校正后试样面积 A（mm²；式 T 1236-2）。ε1 以 % 计。</summary>
    public static double CorrectedAreaMM2(double initialAreaMM2, double axialStrainPercent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialAreaMM2);

        var denominator = 1.0 - axialStrainPercent / 100.0;

        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(axialStrainPercent),
                axialStrainPercent,
                "轴向应变不得达到 100%（式 T 1236-2 的分母会归零）"
            );
        }

        return initialAreaMM2 / denominator;
    }

    /// <summary>试件初始断面积 A0（mm²）——由初始直径算。</summary>
    public static double InitialAreaMM2(double initialDiameterMM) =>
        Math.PI * initialDiameterMM * initialDiameterMM / 4.0;

    /// <summary>轴向应力 σ = P / A（kPa）——"每 0.5mm 增量"的停止判据用它比较（P 以 kN 计，A 以 mm² 计）。</summary>
    public static double AxialStressKPa(double loadKN, double areaMM2) =>
        loadKN * 1_000_000.0 / areaMM2;

    /// <summary>
    /// 规范 4.6 的"荷载不再增加"判据：两相邻采样点之间**每 0.5mm 变形**对应的轴向压力增加 &lt; 5kPa。
    /// （调用方负责取相隔 0.5mm 的两个采样点。）
    /// </summary>
    public static bool IsPlateau(double previousLoadKN, double currentLoadKN, double areaMM2) =>
        AxialStressKPa(currentLoadKN - previousLoadKN, areaMM2) < PlateauStressIncrementKPa;

    /// <summary>
    /// 单件判定：从"轴向变形（mm）- 轴向荷载（kN）"曲线取 Pmax 并算出 σ1max（式 T 1236-1~3）。
    /// 若曲线末端变形超过 15%·h0，按规范 4.6 取变形 15% 处（线性插值）的荷载作最大轴向荷载。
    /// </summary>
    public static ShearStrengthSampleResult AnalyzeSample(
        IReadOnlyList<CurvePoint> curve,
        double initialHeightMM,
        double initialDiameterMM,
        double confiningPressureKPa
    )
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialHeightMM);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialDiameterMM);

        if (curve.Count == 0)
        {
            throw new ArgumentException("曲线为空，无法判定", nameof(curve));
        }

        var strainLimitMM = initialHeightMM * MaxStrainStopPercent / 100.0;
        var last = curve[^1];
        double peakLoadKN;
        double peakDeformationMM;
        var usedStrain15Rule = false;

        if (last.X > strainLimitMM)
        {
            (peakLoadKN, peakDeformationMM) = InterpolateLoadAt(curve, strainLimitMM);
            usedStrain15Rule = true;
        }
        else
        {
            var peak = curve.MaxBy(point => point.Y);
            peakLoadKN = peak.Y;
            peakDeformationMM = peak.X;
        }

        var strainPercent = AxialStrainPercent(peakDeformationMM, initialHeightMM);
        var area = CorrectedAreaMM2(InitialAreaMM2(initialDiameterMM), strainPercent);

        // 式 T 1236-3：σ1max = Pmax(N) / A(mm²) × 10³ + σ3（kPa）
        var shearStrengthKPa = peakLoadKN * 1000.0 / area * 1000.0 + confiningPressureKPa;

        return new ShearStrengthSampleResult(
            peakLoadKN,
            peakDeformationMM,
            strainPercent,
            area,
            confiningPressureKPa,
            shearStrengthKPa,
            usedStrain15Rule
        );
    }

    /// <summary>规范 5.4：一级围压下 3 件剪切强度的算术平均值。</summary>
    public static ShearStrengthLevelResult MeanLevel(double confiningPressureKPa, IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException("该级围压下没有试件结果", nameof(values));
        }

        return new ShearStrengthLevelResult(confiningPressureKPa, values.Average(), values.Count);
    }

    /// <summary>
    /// 规范 5.5.1：由各级（围压 σ3，该级平均剪切强度 σ1max）用最小二乘拟包络线，得 φ、c、r。
    /// 圆心 p=(σ1max+σ3)/2、半径 q=(σ1max−σ3)/2（式 T 1236-6/7/8）。
    /// </summary>
    public static EnvelopeFitResult FitEnvelope(IReadOnlyList<ShearStrengthLevelResult> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);

        if (levels.Count < 2)
        {
            throw new ArgumentException("至少需要 2 级围压才能拟合包络线", nameof(levels));
        }

        var n = levels.Count;
        double sumP = 0,
            sumQ = 0,
            sumPQ = 0,
            sumPP = 0,
            sumQQ = 0;

        foreach (var level in levels)
        {
            var p = (level.MeanShearStrengthKPa + level.ConfiningPressureKPa) / 2.0;
            var q = (level.MeanShearStrengthKPa - level.ConfiningPressureKPa) / 2.0;

            sumP += p;
            sumQ += q;
            sumPQ += p * q;
            sumPP += p * p;
            sumQQ += q * q;
        }

        // 式 T 1236-6：m；式 T 1236-7：b；式 T 1236-8：r
        var numerator = sumPQ - sumP * sumQ / n;
        var denominatorP = sumPP - sumP * sumP / n;
        var denominatorQ = sumQQ - sumQ * sumQ / n;

        if (denominatorP <= 0)
        {
            throw new ArgumentException("各级围压相同，无法拟合包络线", nameof(levels));
        }

        var slope = numerator / denominatorP;
        var intercept = (sumQ - slope * sumP) / n;
        var correlation = numerator / Math.Sqrt(denominatorP * denominatorQ);

        // φ = arcsin m（式 T 1236-4）；|m| ≥ 1 时无解（数据异常）
        var slopeValid = Math.Abs(slope) < 1.0;
        var frictionAngleDegrees = slopeValid ? Math.Asin(slope) * 180.0 / Math.PI : double.NaN;
        var cohesionKPa = slopeValid
            ? intercept / Math.Cos(frictionAngleDegrees * Math.PI / 180.0)
            : double.NaN;

        return new EnvelopeFitResult(
            slope,
            intercept,
            frictionAngleDegrees,
            cohesionKPa,
            correlation,
            slopeValid && correlation >= MinimumCorrelation
        );
    }

    /// <summary>
    /// 规范 5.6（式 T 1236-10）：45°+φ/2 剪切面的主应力 σ_f（kPa）。
    /// 按规范原文写：σ_f = σ3 + ½(σ1 − σ3)·(1 − √(tan²φ/(1+tan²φ)))。（根号项恒等于 sinφ。）
    /// </summary>
    public static double ShearPlaneNormalStressKPa(double sigma1KPa, double sigma3KPa, double frictionAngleDegrees)
    {
        var tan = Math.Tan(frictionAngleDegrees * Math.PI / 180.0);
        var tan2 = tan * tan;

        return sigma3KPa + 0.5 * (sigma1KPa - sigma3KPa) * (1.0 - Math.Sqrt(tan2 / (1.0 + tan2)));
    }

    /// <summary>
    /// 规范 5.7（式 T 1236-12）：45°+φ/2 剪切面上的剪应力 τ_f（kPa）。
    /// </summary>
    public static double ShearStressOnPlaneKPa(double sigma1KPa, double sigma3KPa, double shearPlaneSigmaKPa)
    {
        var radius = (sigma1KPa - sigma3KPa) / 2.0;
        var offset = shearPlaneSigmaKPa - (sigma1KPa + sigma3KPa) / 2.0;
        var inner = radius * radius - offset * offset;

        return inner < 0 ? double.NaN : Math.Sqrt(inner);
    }

    /// <summary>规范 5.6（式 T 1236-9）：τmax = c + σ_f·tanφ（kPa）。</summary>
    public static double MaxShearStrengthKPa(double cohesionKPa, double frictionAngleDegrees, double shearPlaneSigmaKPa)
    {
        var tan = Math.Tan(frictionAngleDegrees * Math.PI / 180.0);

        return cohesionKPa + shearPlaneSigmaKPa * tan;
    }

    /// <summary>
    /// 规范 5.6 / 5.7："必要时"的抗剪强度比 SSR —— 依路面实际应力条件（σ1、σ3）计算 τ_f 与 τmax。
    /// </summary>
    public static SsrResult ShearStrengthRatio(
        double sigma1KPa,
        double sigma3KPa,
        double cohesionKPa,
        double frictionAngleDegrees
    )
    {
        var sigmaF = ShearPlaneNormalStressKPa(sigma1KPa, sigma3KPa, frictionAngleDegrees);
        var tauF = ShearStressOnPlaneKPa(sigma1KPa, sigma3KPa, sigmaF);
        var tauMax = MaxShearStrengthKPa(cohesionKPa, frictionAngleDegrees, sigmaF);

        return new SsrResult(sigmaF, tauF, tauMax, tauF / tauMax);
    }

    private static (double LoadKN, double DeformationMM) InterpolateLoadAt(
        IReadOnlyList<CurvePoint> curve,
        double deformationMM
    )
    {
        for (var i = 1; i < curve.Count; i++)
        {
            if (curve[i].X < deformationMM)
            {
                continue;
            }

            var previous = curve[i - 1];
            var current = curve[i];
            var span = current.X - previous.X;

            if (span <= 0)
            {
                return (current.Y, deformationMM);
            }

            var ratio = (deformationMM - previous.X) / span;

            return (previous.Y + (current.Y - previous.Y) * ratio, deformationMM);
        }

        var lastPoint = curve[^1];

        return (lastPoint.Y, deformationMM);
    }
}
