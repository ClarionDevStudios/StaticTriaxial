namespace ANX.Method.Marshall;

/// <summary>
/// T 0709 抛给界面的**消息资源键**（判定失败原因、试验单必填/格式校验）。
/// 方法层不认识语言：异常里带键 + 中文兜底，界面按当前语言取译文。
///
/// 通用那几条（必填 / 未选 / 格式 / 超范围）与弯曲**共用同一批键**（`Msg.Field*`）——
/// 它们是界面语汇，换了方法也用同一句话。
/// </summary>
public static class MarshallMessageKeys
{
    /// <summary>方法名（执行页标题、参数页副标题）。</summary>
    public const string MethodName = "Method.T0709.Name";

    /// <summary>必填项没填：{0} = 字段名。</summary>
    public const string FieldRequired = "Msg.FieldRequired";

    /// <summary>下拉项没选：{0} = 字段名。</summary>
    public const string FieldChoose = "Msg.FieldChoose";

    /// <summary>格式不对：{0} = 字段名。</summary>
    public const string FieldFormat = "Msg.FieldFormat";

    /// <summary>超出范围：{0} = 字段名、{1} = 下限、{2} = 上限。</summary>
    public const string FieldOutOfRange = "Msg.FieldOutOfRange";

    /// <summary>曲线点数不足以做修正直线拟合。</summary>
    public const string CurveTooFewPoints = "Msg.T0709.CurveTooFewPoints";

    /// <summary>拟合不出修正直线（峰值非正、区间内点不足或斜率为非正）。</summary>
    public const string FitFailed = "Msg.T0709.FitFailed";

    /// <summary>一组至少要有 1 个试件。</summary>
    public const string GroupEmpty = "Msg.T0709.GroupEmpty";

    /// <summary>疑似无效：位移/压力阈值停机、曲线末端仍是最高荷载（未出现峰值）。</summary>
    public const string SuspiciousNoPeak = "Msg.T0709.SuspiciousNoPeak";

    /// <summary>全部键 —— 供语言字典一致性用例把守。</summary>
    public static readonly string[] All =
    [
        MethodName,
        FieldRequired,
        FieldChoose,
        FieldFormat,
        FieldOutOfRange,
        CurveTooFewPoints,
        FitFailed,
        GroupEmpty,
        SuspiciousNoPeak,
    ];

    /// <summary>字段名作为消息参数（嵌一层，界面会按当前语言展开）。</summary>
    public static ANX.Core.LocalizedText FieldName(string displayNameKey) => new(displayNameKey);
}
