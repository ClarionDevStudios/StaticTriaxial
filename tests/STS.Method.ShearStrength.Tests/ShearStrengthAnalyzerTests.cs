namespace STS.Method.ShearStrength.Tests;

using STS.Core;

/// <summary>
/// T 1236—2026 计算部分的黄金用例（式 T 1236-1 ~ T 1236-12）。
/// 数值用手算/独立算术核对过，精度断言取 1e-6 ~ 1e-3（按量级）。
/// </summary>
public sealed class ShearStrengthAnalyzerTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void 轴向应变与校正面积应照规范公式()
    {
        var strain = ShearStrengthAnalyzer.AxialStrainPercent(30.6, 306);
        Assert.Equal(10.0, strain, Tolerance);

        var area0 = ShearStrengthAnalyzer.InitialAreaMM2(150);
        Assert.Equal(Math.PI * 150 * 150 / 4.0, area0, Tolerance);

        var area = ShearStrengthAnalyzer.CorrectedAreaMM2(area0, strain);
        Assert.Equal(area0 / 0.9, area, Tolerance);
    }

    [Fact]
    public void 单件剪切强度应含围压且单位正确()
    {
        // 306mm 高、Φ150mm 试件，变形 30.6mm（ε=10%）时荷载 1.0kN，围压 30kPa
        var curve = new CurvePoint[]
        {
            new(0, 0),
            new(10, 0.4),
            new(20, 0.8),
            new(30.6, 1.0),
        };

        var result = ShearStrengthAnalyzer.AnalyzeSample(curve, 306, 150, 30);

        Assert.False(result.UsedStrain15Rule);
        Assert.Equal(1.0, result.PeakLoadKN, Tolerance);
        Assert.Equal(30.6, result.PeakDeformationMM, Tolerance);
        Assert.Equal(10.0, result.AxialStrainPercent, Tolerance);

        var area0 = Math.PI * 150 * 150 / 4.0;
        var expected = 1000.0 / (area0 / 0.9) * 1000.0 + 30.0;

        Assert.Equal(expected, result.ShearStrengthKPa, 1e-6);
        Assert.Equal(80.9296, result.ShearStrengthKPa, 1e-3);
    }

    [Fact]
    public void 变形超过十五应取十五点处的荷载作最大轴向荷载()
    {
        // h0=300mm → 15% = 45mm；曲线到 50mm，荷载仍上升（1.0kN@50mm）
        var curve = new CurvePoint[]
        {
            new(0, 0),
            new(15, 0.3),
            new(30, 0.6),
            new(45, 0.9),
            new(50, 1.0),
        };

        var result = ShearStrengthAnalyzer.AnalyzeSample(curve, 300, 150, 60);

        Assert.True(result.UsedStrain15Rule);
        Assert.Equal(45.0, result.PeakDeformationMM, Tolerance);
        Assert.Equal(0.9, result.PeakLoadKN, Tolerance);
        Assert.Equal(15.0, result.AxialStrainPercent, Tolerance);
    }

    [Fact]
    public void 未超十五应取曲线峰值荷载()
    {
        var curve = new CurvePoint[]
        {
            new(0, 0),
            new(20, 0.8),
            new(40, 1.2),
            new(45, 1.1),
        };

        var result = ShearStrengthAnalyzer.AnalyzeSample(curve, 300, 150, 60);

        Assert.False(result.UsedStrain15Rule);
        Assert.Equal(1.2, result.PeakLoadKN, Tolerance);
        Assert.Equal(40.0, result.PeakDeformationMM, Tolerance);
    }

    [Fact]
    public void 三级精确点的包络拟合应与手算一致()
    {
        // 构造"q = 0.5p + 10"的精确点：σ1 = 3σ3 + 40 → (30,130) (60,220) (100,340)
        var levels = new ShearStrengthLevelResult[]
        {
            new(30, 130, 3),
            new(60, 220, 3),
            new(100, 340, 3),
        };

        var fit = ShearStrengthAnalyzer.FitEnvelope(levels);

        Assert.Equal(0.5, fit.Slope, 1e-9);
        Assert.Equal(10.0, fit.Intercept, 1e-9);
        Assert.Equal(30.0, fit.FrictionAngleDegrees, 1e-9);
        Assert.Equal(10.0 / Math.Cos(Math.PI / 6), fit.CohesionKPa, 1e-9);
        Assert.Equal(1.0, fit.Correlation, 1e-9);
        Assert.True(fit.IsReliable);
    }

    [Fact]
    public void 相关性不足时应有可靠性标志()
    {
        // (30,130) (60,220) (100,290)：r ≈ 0.98641 < 0.99
        var levels = new ShearStrengthLevelResult[]
        {
            new(30, 130, 3),
            new(60, 220, 3),
            new(100, 290, 3),
        };

        var fit = ShearStrengthAnalyzer.FitEnvelope(levels);

        Assert.Equal(0.98641, fit.Correlation, 1e-4);
        Assert.Equal(0.39295, fit.Slope, 1e-4);
        Assert.Equal(23.14, fit.FrictionAngleDegrees, 0.01);
        Assert.Equal(22.45, fit.CohesionKPa, 0.01);
        Assert.False(fit.IsReliable);
    }

    [Fact]
    public void 抗剪强度比应按规范公式计算()
    {
        var phi = 30.0;
        var cohesion = 10.0 / Math.Cos(Math.PI / 6);

        var result = ShearStrengthAnalyzer.ShearStrengthRatio(200, 50, cohesion, phi);

        // σf = 50 + ½(150)(1−sin30°) = 87.5
        Assert.Equal(87.5, result.ShearPlaneNormalStressKPa, 1e-9);
        // τf = √(75² − (87.5−125)²) = √4218.75 ≈ 64.951905
        Assert.Equal(Math.Sqrt(4218.75), result.ShearStressOnPlaneKPa, 1e-9);
        // τmax = c + 87.5·tan30° ≈ 62.065154
        Assert.Equal(cohesion + 87.5 * Math.Tan(Math.PI / 6), result.MaxShearStrengthKPa, 1e-9);
        Assert.Equal(1.04651, result.Ssr, 1e-4);
    }

    [Fact]
    public void 停止判据原语应可直接使用()
    {
        var area0 = ShearStrengthAnalyzer.InitialAreaMM2(150);

        // 1.0kN / 19634.95mm² → 50.9296kPa
        Assert.Equal(50.9296, ShearStrengthAnalyzer.AxialStressKPa(1.0, area0 / 0.9), 1e-3);

        // 0.9→0.95kN（Δσ≈2.55kPa）→ 平台（停止）；0.9→1.0kN（Δσ≈5.09kPa）→ 未达平台
        Assert.True(ShearStrengthAnalyzer.IsPlateau(0.9, 0.95, area0 / 0.9));
        Assert.False(ShearStrengthAnalyzer.IsPlateau(0.9, 1.0, area0 / 0.9));
    }
}
