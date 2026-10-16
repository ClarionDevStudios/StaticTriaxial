namespace STS.Method.ShearStrength;

using STS.Control;

/// <summary>停止原因（T 1236—2026 规范 4.6）。</summary>
public enum ShearStopReason
{
    None = 0,

    /// <summary>"每 0.5mm 变形对应的轴向压力增加小于 5kPa"（荷载不再增加）。</summary>
    LoadPlateau = 1,

    /// <summary>荷载读数降低。</summary>
    LoadDropped = 2,

    /// <summary>轴向变形超过 15%（取 15% 变形处荷载作最大轴向荷载）。</summary>
    StrainLimit = 3,
}

/// <summary>一次"是否停止"的判定结果。</summary>
public sealed record ShearStopDecision(bool ShouldStop, ShearStopReason Reason);

/// <summary>
/// T 1236—2026 规范 4.6 的停止（破坏）判据 —— 按采样逐拍喂入（轴向变形 mm、轴向荷载 kN）的状态机：
/// ①"每 0.5mm 变形对应的轴向压力增加小于 5kPa"或荷载降低 → 停止（最大荷载作 Pmax）；
/// ②轴向变形超过 15%·h0 → 停止（取 15% 变形处的荷载作 Pmax）。
///
/// 轴向压力按"**校正后面积**的轴向应力"计算（与式 T 1236-3 同口径；判定器
/// <see cref="ShearStrengthAnalyzer"/> 事后按同一口径复核 Pmax——两条路同一套公式）。
/// 0.5mm 窗口 = 与"最近一条 0.5mm 之前的记录"比较，采样记录按变形每推进 0.5mm 记一条。
/// </summary>
public sealed class ShearStrengthStopCriteria(double initialHeightMM, double initialDiameterMM)
    : ITestStopCriteria
{
    /// <summary>增量窗口：规范 4.6 的"每 0.5mm"。</summary>
    public const double PlateauIntervalMM = 0.5;

    private readonly List<(double DeformationMM, double StressKPa)> _samples = [];

    private readonly double _initialAreaMM2 = ShearStrengthAnalyzer.InitialAreaMM2(
        initialDiameterMM
    );

    private readonly double _strainLimitMM =
        initialHeightMM * ShearStrengthAnalyzer.MaxStrainStopPercent / 100.0;

    /// <summary>喂一拍采样（只给位移与荷载；用例与内部走这条），返回是否应停止。</summary>
    public ShearStopDecision Evaluate(double deformationMM, double loadKN) =>
        EvaluateCore(deformationMM, loadKN);

    /// <inheritdoc />
    TestStopDecision ITestStopCriteria.Evaluate(
        double displacementMM,
        double loadKN,
        double elapsedSeconds
    )
    {
        var decision = EvaluateCore(displacementMM, loadKN);

        return new TestStopDecision(decision.ShouldStop, decision.Reason.ToString());
    }

    private ShearStopDecision EvaluateCore(double deformationMM, double loadKN)
    {
        // ② 15% 应变上限：优先判定（规范 4.6 后半句）
        if (deformationMM > _strainLimitMM)
        {
            return new ShearStopDecision(true, ShearStopReason.StrainLimit);
        }

        var strainPercent = ShearStrengthAnalyzer.AxialStrainPercent(
            deformationMM,
            initialHeightMM
        );
        var area = ShearStrengthAnalyzer.CorrectedAreaMM2(_initialAreaMM2, strainPercent);
        var stressKPa = ShearStrengthAnalyzer.AxialStressKPa(loadKN, area);

        // 记录 0.5mm 采样点（每推进 0.5mm 记一条）
        if (
            _samples.Count == 0
            || deformationMM - _samples[^1].DeformationMM >= PlateauIntervalMM
        )
        {
            _samples.Add((deformationMM, stressKPa));
        }

        // ① 与最近一条"至少 0.5mm 之前"的记录比较
        for (var i = _samples.Count - 1; i >= 0; i--)
        {
            if (deformationMM - _samples[i].DeformationMM < PlateauIntervalMM)
            {
                continue;
            }

            var delta = stressKPa - _samples[i].StressKPa;

            if (delta < 0)
            {
                return new ShearStopDecision(true, ShearStopReason.LoadDropped);
            }

            if (delta < ShearStrengthAnalyzer.PlateauStressIncrementKPa)
            {
                return new ShearStopDecision(true, ShearStopReason.LoadPlateau);
            }

            break;
        }

        return new ShearStopDecision(false, ShearStopReason.None);
    }
}
