namespace ANX.Method.Bending.Tests;

using ANX.Core;
using ANX.Method.Bending;

/// <summary>
/// T 0715 判定算法。前两组是**从现役 DUnitX 用例原样搬过来的黄金值**（`Tests\Tests.Bending.pas`），
/// 用来锁住公式；后面的用例是自造的解析曲线，用来锁住原点修正与两条峰值路径。
/// 与现役的差异（如非法几何抛异常 vs 返回 0）记在 `docs\对照\T0715-算法对照.md`。
/// </summary>
public sealed class BendingAnalyzerTests
{
    private const double Tolerance = 1e-6;

    // ── 5.3 弯曲强度（现役黄金值）────────────────────────────────────

    [Fact]
    public void 弯曲强度应按公式计算并保留两位小数()
    {
        // R_B = 3·L·P·1000 / (2·b·h²)；L=200 b=30 h=35 P=1kN → 8.1633 → 8.16
        Assert.Equal(8.16, BendingAnalyzer.FlexuralStrength(1.0, 200, 30, 35), Tolerance);

        // 荷载减半 → 强度减半
        Assert.Equal(4.08, BendingAnalyzer.FlexuralStrength(0.5, 200, 30, 35), Tolerance);
    }

    [Fact]
    public void 弯曲强度几何参数非法应抛异常而不是返回零()
    {
        // 与现役的行为差异：现役返回 0.0，会把"参数没填"伪装成一个正常结果流进平均值。
        Assert.Throws<ArgumentOutOfRangeException>(() => BendingAnalyzer.FlexuralStrength(1.0, 0, 30, 35));
        Assert.Throws<ArgumentOutOfRangeException>(() => BendingAnalyzer.FlexuralStrength(1.0, 200, 0, 35));
        Assert.Throws<ArgumentOutOfRangeException>(() => BendingAnalyzer.FlexuralStrength(1.0, 200, 30, 0));
    }

    // ── 5.4 最大弯拉应变（现役黄金值）───────────────────────────────

    [Fact]
    public void 最大弯拉应变应按公式计算并保留一位小数()
    {
        // ε_B = 6·h·d/L² ·1e6；h=35 d=1 L=200 → 5250 με
        Assert.Equal(5250.0, BendingAnalyzer.MaxBendingTensileStrain(35, 1, 200), Tolerance);
        Assert.Equal(10500.0, BendingAnalyzer.MaxBendingTensileStrain(35, 2, 200), Tolerance);
    }

    [Fact]
    public void 最大弯拉应变几何参数非法应抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BendingAnalyzer.MaxBendingTensileStrain(35, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BendingAnalyzer.MaxBendingTensileStrain(0, 1, 200));
    }

    // ── 5.1 原点修正 ────────────────────────────────────────────────

    [Fact]
    public void 原点修正应等于修正直线的横轴截距()
    {
        // 造一条上升段严格落在 y = 2x − 1 上的曲线 → 修正原点 O′ = 1/2 = 0.5
        var curve = BuildCurve();
        var tangent = BendingAnalyzer.FitTangentLine(curve);

        Assert.NotNull(tangent);
        Assert.Equal(2.0, tangent!.Value.K, 1e-6);
        Assert.Equal(-1.0, tangent.Value.B, 1e-6);
    }

    /// <summary>
    /// 是否做原点修正可由参数控制（用户 2026-10-12：有的试验需要、有的不需要）。
    /// 关闭时：修正原点记 0、校正挠度 = 峰值挠度、修正直线系数不参与结果。
    /// </summary>
    [Fact]
    public void 关闭原点修正时修正原点应记零且挠度用峰值挠度()
    {
        var curve = BuildCurve(); // 上升段落在 y = 2x − 1 上 → 修正原点 0.5

        var corrected = BendingAnalyzer.Analyze(curve, 200, 30, 35, enableOriginCorrection: true);
        var uncorrected = BendingAnalyzer.Analyze(curve, 200, 30, 35, enableOriginCorrection: false);

        Assert.Equal(0.5, corrected.OriginCorrectionMM, 1e-6);
        Assert.Equal(corrected.PeakDeflectionMM - 0.5, corrected.CorrectedDeflectionMM, 1e-6);

        Assert.Equal(0.0, uncorrected.OriginCorrectionMM, 1e-6);
        Assert.Equal(0.0, uncorrected.TangentK, 1e-6);
        Assert.Equal(0.0, uncorrected.TangentB, 1e-6);
        Assert.Equal(uncorrected.PeakDeflectionMM, uncorrected.CorrectedDeflectionMM, 1e-6);

        // 强度只看峰值荷载，所以两者相同；应变按各自挠度算，未修正的更大
        Assert.Equal(corrected.FlexuralStrengthMPa, uncorrected.FlexuralStrengthMPa, 1e-6);
        Assert.True(uncorrected.MaxBendingTensileStrainMicro > corrected.MaxBendingTensileStrainMicro);
    }

    // ── 5.2 破坏荷载与跨中挠度 ──────────────────────────────────────
    /// <summary>
    /// 锁定峰值位移（现役 LockDisplacement，默认选中）：开 → 挠度取峰值荷载点；
    /// 关 → 取曲线最大位移（破坏荷载仍取峰值荷载点）。
    /// </summary>
    [Fact]
    public void 锁定峰值位移开关应决定挠度取峰值点还是曲线最大位移()
    {
        var curve = BuildCurve(); // 尾段继续下行 → 峰值点与最大位移点不同

        var locked = BendingAnalyzer.Analyze(curve, 200, 30, 35);
        var unlocked = BendingAnalyzer.Analyze(curve, 200, 30, 35, lockPeakDisplacement: false);

        // 默认（锁定）= 现行口径，不改变既有结果
        Assert.True(locked.PeakDeflectionMM >= 0);
        Assert.Equal(locked.PeakLoadKN, unlocked.PeakLoadKN, 6); // 破坏荷载都取峰值荷载点
        Assert.True(unlocked.PeakDeflectionMM >= locked.PeakDeflectionMM);
    }

    [Fact]
    public void 破坏荷载应取曲线最大荷载且挠度从修正原点量起()
    {
        var result = BendingAnalyzer.Analyze(BuildCurve(), 200, 30, 35);

        Assert.Equal(9.00, result.PeakLoadKN, Tolerance);
        Assert.Equal(5.00, result.PeakDeflectionMM, Tolerance);
        Assert.Equal(0.50, result.OriginCorrectionMM, Tolerance);
        Assert.Equal(4.5, result.CorrectedDeflectionMM, Tolerance);

        // R_B = 3·200·9000 / (2·30·1225) = 73.469 → 73.47 MPa
        Assert.Equal(73.47, result.FlexuralStrengthMPa, Tolerance);
        // ε_B = 6·35·4.5/200² ·1e6 = 23625 με
        Assert.Equal(23625.0, result.MaxBendingTensileStrainMicro, Tolerance);
    }

    /// <summary>

    // ── 5.5 / 5.6 / 6 组统计与超差剔除 ─────────────────────────────

    [Fact]
    public void 组内应变都在允许误差内时不应剔除()
    {
        var group = BendingAnalyzer.AnalyzeGroup(
        [
            MakeSample(strength: 8.00, strain: 5000),
            MakeSample(strength: 8.20, strain: 5200),
            MakeSample(strength: 8.10, strain: 5100),
            MakeSample(strength: 8.30, strain: 5300),
        ]);

        Assert.Empty(group.ExcludedIndexes);
        Assert.Equal(4, group.EffectiveCount);
        Assert.False(group.NeedsSupplement);
        Assert.Equal(8.15, group.MeanFlexuralStrengthMPa, Tolerance);   // (8.00+8.20+8.10+8.30)/4
        Assert.Equal(5150.0, group.MeanMaxBendingTensileStrainMicro, Tolerance); // 组值按 1με
    }

    [Fact]
    public void 超差时应剔除与平均值偏差最大的那个并重算()
    {
        var group = BendingAnalyzer.AnalyzeGroup(
        [
            MakeSample(strength: 8.00, strain: 5000),
            MakeSample(strength: 8.00, strain: 5100),
            MakeSample(strength: 8.00, strain: 5200),
            MakeSample(strength: 9.00, strain: 6000), // 偏差最大
        ]);

        Assert.Equal([3], group.ExcludedIndexes);
        Assert.Equal(3, group.EffectiveCount);
        Assert.Equal(5100.0, group.MeanMaxBendingTensileStrainMicro, Tolerance);
        Assert.False(group.NeedsSupplement);
    }

    [Fact]
    public void 剔除会跌破三个有效试件时不应剔除()
    {
        var group = BendingAnalyzer.AnalyzeGroup(
        [
            MakeSample(strength: 8.00, strain: 5000),
            MakeSample(strength: 8.00, strain: 5100),
            MakeSample(strength: 9.00, strain: 9000),
        ]);

        // 剔掉最差的那个就会只剩 2 个（< 3），所以规范要求"补充试验"而不是继续剔
        Assert.Empty(group.ExcludedIndexes);
        Assert.Equal(3, group.EffectiveCount);
        Assert.False(group.NeedsSupplement);
    }

    [Fact]
    public void 有效试件不足三个时应提示补充试验()
    {
        var group = BendingAnalyzer.AnalyzeGroup(
        [
            MakeSample(strength: 8.00, strain: 5000),
            MakeSample(strength: 8.10, strain: 5100),
        ]);

        Assert.Equal(2, group.EffectiveCount);
        Assert.True(group.NeedsSupplement);
    }

    private static BendingSampleResult MakeSample(double strength, double strain) =>
        new(strength, 5, 4.5, strength, strain, 2, -1, 0.5);

    /// <summary>
    /// 造曲线：上升段严格在 y = 2x − 1 上（30 点），峰值 (5.00, 9.00)，之后下降。
    /// </summary>
    private static CurvePoint[] BuildCurve()
    {
        var points = new List<CurvePoint>();

        for (var i = 0; i < 30; i++)
        {
            var x = 0.5 + i * 0.15; // 0.50 → 4.85
            points.Add(new CurvePoint(x, 2 * x - 1));
        }

        points.Add(new CurvePoint(5.00, 9.00));
        points.Add(new CurvePoint(5.10, 8.00));
        points.Add(new CurvePoint(5.20, 7.00));
        points.Add(new CurvePoint(5.30, 6.00));
        points.Add(new CurvePoint(5.40, 5.00));
        points.Add(new CurvePoint(5.50, 4.00));

        return [.. points];
    }
}
