namespace STS.Method.ShearStrength;

using STS.Control;

/// <summary>
/// T 1236 的**试验单字段**声明（报告 6.1~6.6 + 人员；试验单弹窗按它渲染）。
/// 顺序 = 弹窗里的摆放顺序（界面两列按行配对）：左列工程 / 样品信息，右列接样·试验条件 /
/// 仪器成型 / 人员。`SheetID` 与 `CreatedOn` 是**自动值行**（只读展示：单号预览、试验日期），
/// 由界面填、不入提交（建单 / 改单时会剔掉）。
/// </summary>
public static class ShearStrengthSheetFields
{
    public const string SheetIdKey = "SheetID";

    public const string ProjectNameKey = "ProjectName";

    public const string SampleNumberKey = "SampleNumber";

    public const string SampleNameKey = "SampleName";

    public const string SampleOriginKey = "SampleOrigin";

    public const string SampleSpecificationKey = "SampleSpecification";

    public const string SampleReceiptDateKey = "SampleReceiptDate";

    public const string SampleDescriptionKey = "SampleDescription";

    public const string CreatedOnKey = "CreatedOn";

    public const string SampleReductionMethodKey = "SampleReductionMethod";

    public const string MachineNameAndIDKey = "MachineNameAndID";

    public const string MoldingMethodKey = "MoldingMethod";

    public const string MoldingParametersKey = "MoldingParameters";

    public const string TestPersonKey = "TestPerson";

    public const string CalculatePersonKey = "CalculatePerson";

    public const string VerificationPersonKey = "VerificationPerson";

    /// <summary>必填项（方法层在新建 / 改单时校验；显示名带 ` *` 提示）。只要求项目名称，其余可留空。</summary>
    public static readonly IReadOnlyList<string> RequiredKeys = [ProjectNameKey];

    /// <summary>字段声明（含 `SheetID` / `CreatedOn` 两个只读展示行）。</summary>
    public static readonly IReadOnlyList<ParameterDefinition> Definitions =
        [.. RawDefinitions().Select(definition => definition.WithTextKeys())];

    private static IReadOnlyList<ParameterDefinition> RawDefinitions() =>
    [
        Text(ProjectNameKey, "项目名称 *", "报告 6.1：试验项目名称（必填）"),
        Text(SheetIdKey, "试验单号", "自动生成（只读）"),
        Text(SampleNumberKey, "样品编号", "报告 6.2"),
        Text(SampleNameKey, "样品名称", "报告 6.2（如：级配碎石）"),
        Text(SampleOriginKey, "样品产地", "报告 6.2"),
        Text(SampleSpecificationKey, "规格", "报告 6.2（如：0~31.5mm）"),
        Date(SampleReceiptDateKey, "接样日期", "报告 6.3（yyyy-MM-dd）"),
        Text(SampleDescriptionKey, "样品描述", "报告 6.3（自由文本）"),
        Text(CreatedOnKey, "试验日期", "新建时填当天日期（只读；库里存完整时间戳）"),
        Text(SampleReductionMethodKey, "样品缩分方法", "报告 6.4（可存模板复用）"),
        Text(MachineNameAndIDKey, "仪器设备名称、型号及编号", "报告 6.5（可存模板复用）"),
        Text(MoldingMethodKey, "成型方法", "报告 6.6（按本规程 T 1227）"),
        Text(MoldingParametersKey, "成型主要参数", "报告 6.6（如分层数 / 击实功 / 目标含水率）"),
        Text(TestPersonKey, "试验员", "可存模板复用"),
        Text(CalculatePersonKey, "计算人", "可存模板复用"),
        Text(VerificationPersonKey, "校核人", "可存模板复用"),
    ];

    private static ParameterDefinition Text(string key, string displayName, string description) =>
        new(
            key,
            displayName,
            string.Empty,
            string.Empty,
            ParameterGroup.Sheet,
            ParameterKind.Text,
            Scope: ParameterScope.Computation,
            Description: description
        );

    private static ParameterDefinition Date(string key, string displayName, string description) =>
        new(
            key,
            displayName,
            string.Empty,
            string.Empty,
            ParameterGroup.Sheet,
            ParameterKind.Date,
            Scope: ParameterScope.Computation,
            Description: description
        );
}
