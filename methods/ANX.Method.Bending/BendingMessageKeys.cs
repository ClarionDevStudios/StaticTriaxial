namespace ANX.Method.Bending;

/// <summary>
/// T 0715 抛给界面的**消息资源键**（判定失败原因、试验单必填/格式校验）。
/// 方法层不认识语言：异常里带键 + 中文兜底，界面按当前语言取译文（见 <c>ANX.Ui.Services.UiText</c>）。
///
/// 参数里可以嵌字段名的键（例：「{0}不能为空」的 {0} 就是字段自己的
/// <c>Field.Sheet.*</c>），界面递归解析 —— 所以"哪一项"和"什么毛病"各自跟语言走。
/// </summary>
public static class BendingMessageKeys
{
    /// <summary>方法名（执行页标题、参数页副标题）。</summary>
    public const string MethodName = "Method.T0715.Name";

    /// <summary>必填项没填：{0} = 字段名。</summary>
    public const string FieldRequired = "Msg.FieldRequired";

    /// <summary>下拉项没选：{0} = 字段名。</summary>
    public const string FieldChoose = "Msg.FieldChoose";

    /// <summary>格式不对：{0} = 字段名。</summary>
    public const string FieldFormat = "Msg.FieldFormat";

    /// <summary>超出范围：{0} = 字段名、{1} = 下限、{2} = 上限。</summary>
    public const string FieldOutOfRange = "Msg.FieldOutOfRange";

    /// <summary>曲线点数不足以做原点修正。</summary>
    public const string CurveTooFewPoints = "Msg.T0715.CurveTooFewPoints";

    /// <summary>拟合不出修正直线（峰值非正或上升段不足）。</summary>
    public const string FitFailed = "Msg.T0715.FitFailed";

    /// <summary>一组至少要有 1 个试件。</summary>
    public const string GroupEmpty = "Msg.T0715.GroupEmpty";

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
    ];

    /// <summary>字段名作为消息参数（嵌一层，界面会按当前语言展开）。</summary>
    public static ANX.Core.LocalizedText FieldName(string displayNameKey) => new(displayNameKey);
}
