namespace ANX.Method.Marshall;

using ANX.Control;

/// <summary>
/// T 0709 实时指标卡与曲线图用到的资源键。文案在界面层的语言资源里，这里只放键。
/// </summary>
public static class MarshallMetricKeys
{
    /// <summary>实时荷载（kN）。</summary>
    public const string Load = "Method.T0709.Load";

    /// <summary>实时变形（mm）。</summary>
    public const string Displacement = "Method.T0709.Displacement";

    /// <summary>稳定度 MS（kN，判定值）。</summary>
    public const string Stability = "Method.T0709.Stability";

    /// <summary>流值 FL（mm，判定值）。</summary>
    public const string FlowValue = "Method.T0709.FlowValue";

    /// <summary>马歇尔模数 T（kN/mm）。</summary>
    public const string MarshallModulus = "Method.T0709.MarshallModulus";

    /// <summary>修正原点 O′（mm）。</summary>
    public const string OriginCorrection = "Method.T0709.OriginCorrection";

    /// <summary>峰值荷载（明细列 <c>LoadPeakValue</c>）。</summary>
    public const string PeakLoad = "Method.T0709.PeakLoad";

    /// <summary>试验持续时间（s）。</summary>
    public const string Elapsed = "Method.T0709.Elapsed";

    /// <summary>曲线图的图题（带试件号占位符）。</summary>
    public const string CurveTitle = "Method.T0709.CurveTitle";

    /// <summary>历史数据页「试件」列表的试件号列名。</summary>
    public const string SampleId = "Method.T0709.SampleId";

    /// <summary>「异常值判断」列：合格（照现役 T0709 报表用「合格」/「剔除」）。</summary>
    public const string ValidityNormal = "Method.T0709.ValidityNormal";

    /// <summary>「异常值判断」列：剔除（稳定度偏离本组均值 15% 以上）。</summary>
    public const string ValidityAbnormal = "Method.T0709.ValidityAbnormal";

    /// <summary>报表「试验类型」列：标准马歇尔试验。</summary>
    public const string TestTypeStandard = "Method.T0709.TestTypeStandard";

    /// <summary>报表「试验类型」列：浸水马歇尔试验（第二组）。</summary>
    public const string TestTypeImmersion = "Method.T0709.TestTypeImmersion";

    /// <summary>报表「试验类型」列：真空饱水马歇尔试验（第二组）。</summary>
    public const string TestTypeVacuum = "Method.T0709.TestTypeVacuum";

    /// <summary>
    /// 全部键 —— 供"语言字典里是否有这些键"的一致性检查使用。
    /// 含 <see cref="CurveMarkKeys.All"/> 与两条轴名：那几条键由界面与报表共用，一并纳入检查。
    /// </summary>
    public static readonly string[] All =
    [
        Load,
        Displacement,
        Stability,
        FlowValue,
        MarshallModulus,
        OriginCorrection,
        PeakLoad,
        Elapsed,
        CurveTitle,
        SampleId,
        ValidityNormal,
        ValidityAbnormal,
        TestTypeStandard,
        TestTypeImmersion,
        TestTypeVacuum,
        MarshallCurveAxes.Deformation,
        MarshallCurveAxes.Load,
        .. CurveMarkKeys.All,
    ];
}
