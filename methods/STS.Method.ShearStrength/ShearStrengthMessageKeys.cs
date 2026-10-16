namespace STS.Method.ShearStrength;

using STS.Core;

/// <summary>
/// T 1236 抛给界面的**消息资源键**（方法层不认识语言：异常里带键 + 中文兜底，
/// 界面按当前语言取译文，见 <c>STS.Ui.Services.UiText</c>）。
/// 通用句式（必填 / 格式 / 范围）复用框架级的 `Msg.Field*` 键，不重复定义。
/// </summary>
public static class ShearStrengthMessageKeys
{
    /// <summary>方法名（执行页标题、参数页副标题）。</summary>
    public const string MethodName = "Method.ShearStrength.Name";

    /// <summary>必填没填：{0} = 字段名（复用框架级句式）。</summary>
    public const string FieldRequired = "Msg.FieldRequired";

    /// <summary>格式不对：{0} = 字段名（复用框架级句式）。</summary>
    public const string FieldFormat = "Msg.FieldFormat";

    /// <summary>超出范围：{0} = 字段名、{1} = 下限、{2} = 上限（复用框架级句式）。</summary>
    public const string FieldOutOfRange = "Msg.FieldOutOfRange";

    /// <summary>缺试件尺寸（开始试验前的硬校验）：{0} = 试件号。</summary>
    public const string MissingSpecimenSize = "Msg.ShearStrength.MissingSpecimenSize";

    /// <summary>收尾：围压级数不足（{0} = 当前级数、{1} = 要求级数）。</summary>
    public const string NeedMoreLevels = "Msg.ShearStrength.NeedMoreLevels";

    /// <summary>收尾：某级件数不足（{0} = σ3、{1} = 当前件数、{2} = 要求件数）。</summary>
    public const string LevelIncomplete = "Msg.ShearStrength.LevelIncomplete";

    /// <summary>运行起点提示：试件高度超出合格窗（{0} = 实测高度、{1} = 标准高度、{2} = 容差）。</summary>
    public const string HeightOutOfWindow = "Msg.ShearStrength.HeightOutOfWindow";

    /// <summary>全部键 —— 供语言字典一致性用例把守。</summary>
    public static readonly string[] All =
    [
        MethodName,
        FieldRequired,
        FieldFormat,
        FieldOutOfRange,
        MissingSpecimenSize,
        NeedMoreLevels,
        LevelIncomplete,
        HeightOutOfWindow,
    ];

    /// <summary>字段名作为消息参数（嵌一层，界面按当前语言展开）。</summary>
    public static LocalizedText FieldName(string displayNameKey) => new(displayNameKey);
}
