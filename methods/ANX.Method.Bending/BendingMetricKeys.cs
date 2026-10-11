namespace ANX.Method.Bending;

/// <summary>
/// T 0715 实时指标卡与曲线图用到的资源键。文案在界面层的语言资源里，这里只放键
/// （IAP 的 `FieldDefinition.DisplayNameKey` 同款做法）。
/// </summary>
public static class BendingMetricKeys
{
    public const string Load = "Method.T0715.Load";

    public const string Displacement = "Method.T0715.Displacement";

    public const string FlexuralStrength = "Method.T0715.FlexuralStrength";

    public const string OriginCorrection = "Method.T0715.OriginCorrection";

    public const string MaxStrain = "Method.T0715.MaxStrain";

    public const string CorrectedDeflection = "Method.T0715.CorrectedDeflection";

    public const string Elapsed = "Method.T0715.Elapsed";

    /// <summary>曲线图的图题（带试件号占位符）。</summary>
    public const string CurveTitle = "Method.T0715.CurveTitle";

    /// <summary>全部键 —— 供"语言字典里是否有这些键"的一致性检查使用。</summary>
    public static readonly string[] All =
    [
        Load,
        Displacement,
        FlexuralStrength,
        OriginCorrection,
        MaxStrain,
        CorrectedDeflection,
        Elapsed,
        CurveTitle,
        BendingCurveAxes.Deflection,
        BendingCurveAxes.Load,
    ];
}
