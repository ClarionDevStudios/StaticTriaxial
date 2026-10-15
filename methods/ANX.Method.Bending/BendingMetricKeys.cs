namespace ANX.Method.Bending;

using ANX.Control;

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

    /// <summary>
    /// 历史数据页「试件」列表与字段表的**列名**（用户 2026-10-15："SampleID 及列名信息要和当前语言一致"）。
    /// 明细表里那几列有现成的指标名就复用（跨中挠度 / 弯拉强度 / 最大弯拉应变），剩下的在这里补。
    /// </summary>
    public const string SampleId = "Method.T0715.SampleId";

    /// <summary>试件宽度（明细列 <c>SampleWidth</c>）。</summary>
    public const string SampleWidth = "Method.T0715.SampleWidth";

    /// <summary>试件高度（明细列 <c>SampleHeight</c>）。</summary>
    public const string SampleHeight = "Method.T0715.SampleHeight";

    /// <summary>峰值荷载（明细列 <c>PeakLoadValue</c>）。</summary>
    public const string PeakLoad = "Method.T0715.PeakLoad";

    /// <summary>「异常值判断」列：正常（现役报表写「正常」/「异常」）。</summary>
    public const string ValidityNormal = "Method.T0715.ValidityNormal";

    /// <summary>「异常值判断」列：异常（已被 5.6 剔除）。</summary>
    public const string ValidityAbnormal = "Method.T0715.ValidityAbnormal";

    /// <summary>
    /// 全部键 —— 供"语言字典里是否有这些键"的一致性检查使用。
    /// 含 <see cref="CurveMarkKeys.All"/>（曲线图标注文案）：那三条键由界面与报表共用，
    /// 所以一并纳入检查，免得漏翻时"实时曲线有文字、存盘图变成键名"。
    /// </summary>
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
        ValidityNormal,
        ValidityAbnormal,
        SampleId,
        SampleWidth,
        SampleHeight,
        PeakLoad,
        BendingCurveAxes.Deflection,
        BendingCurveAxes.Load,
        .. CurveMarkKeys.All,
    ];
}
