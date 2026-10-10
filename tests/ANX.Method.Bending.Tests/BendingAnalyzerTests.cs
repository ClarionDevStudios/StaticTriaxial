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

    // ── 5.2 破坏荷载与跨中挠度 ──────────────────────────────────────

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
