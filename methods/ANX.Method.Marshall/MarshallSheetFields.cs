namespace ANX.Method.Marshall;

using ANX.Control;

/// <summary>
/// **试验单信息**的字段声明（试验单编辑窗体用）。键 = 主表列名（框架按列名写库）。
///
/// 字段与次序照现役马歇尔试验单窗（`uEditRoadMarshallStabilityTestForm.dfm` 的控件版面；它同时是
/// 混合料与路面芯样两种马歇尔的试验单窗）：工程信息在前、体积指标在后，两列**按行配对**
/// （与弯曲同一套版面约定，界面用两列 UniformGrid 按行填充）。
///
/// 口径：
/// <list type="bullet">
///   <item>「试验依据」决定「试验方法」与「试件尺寸」的可选项（现役 <c>cbbTestSpecificationsChange</c>：
///         混合料 → 标准/浸水/真空饱水 + φ101.6×63.5 / φ152.4×95.3；路面芯样 → 只有标准马歇尔，
///         尺寸 φ100×(30~80) / φ150×(80~100)）—— 界面靠 <c>ResolveDependentDefaults</c> 联动；</item>
///   <item>带 <c>Desc</c> 的列（试验依据/试验方法/试件尺寸的描述）由创建/保存时按所选**代号**自动写，
///         报表取的就是这几列文字；</item>
///   <item>体积指标 6 项**允许留空**（照现役与弯曲）。</item>
/// </list>
/// </summary>
public static class MarshallSheetFields
{
    /// <summary>试验依据（0 = T0709-2025 混合料马歇尔、1 = T0710-2011 路面芯样马歇尔）。</summary>
    public const string SpecificationKey = "TestSpecificationsID";

    /// <summary>试验方法（0 = 标准、1 = 浸水、2 = 真空饱水；芯样只有 0）。</summary>
    public const string StandardsKey = "TestStandardsID";

    /// <summary>试件尺寸档（0/1 两档，文字随试验依据不同）。</summary>
    public const string SampleSizeKey = "SampleSizeID";

    /// <summary>试验项目名称：必填。</summary>
    public const string ProjectNameKey = "ProjectName";

    /// <summary>试验温度（℃）：60℃ 是标准试件的养生/试验温度（规范 T 0709）。</summary>
    public const string TemperatureKey = "TestTemperatureDesc";

    /// <summary>混合料类型（现役 AsphaltMixtureType）。</summary>
    public const string MixtureTypeKey = "AsphaltMixtureType";

    /// <summary>试验依据选项（现役下拉框：混合料 / 路面芯样；T0767 冻融未纳入本版）。</summary>
    public static readonly string[] SpecificationLabels =
    [
        "T 0709-2025 沥青混合料马歇尔稳定度试验",
        "T 0710-2011 沥青路面芯样马歇尔稳定度试验",
    ];

    /// <summary>混合料马歇尔的试验方法选项（0/1/2 与现役 TestStandardsID 值域一致）。</summary>
    public static readonly string[] StandardsLabels =
    [
        "标准马歇尔试验",
        "浸水马歇尔试验",
        "真空饱水马歇尔试验",
    ];

    /// <summary>混合料马歇尔的试件尺寸档（照现役 cbbSampleSize）。</summary>
    public static readonly string[] SampleSizeLabels =
    [
        "Φ101.6 × 63.5mm",
        "Φ152.4 × 95.3mm",
    ];

    /// <summary>路面芯样马歇尔的试件尺寸档（照现役：φ100×(30~80) / φ150×(80~100)）。</summary>
    public static readonly string[] CoreSampleSizeLabels =
    [
        "Φ100 × (30~80)mm",
        "Φ150 × (80~100)mm",
    ];

    /// <summary>
    /// 试验单字段的显示文本资源键前缀。**自成一组**（不复用 <see cref="ParameterTextKeys"/>）：
    /// 试验单上的名字带 <c> *</c> 标必填，与参数表里同一项的措辞也不同，混用键会互相牵扯。
    /// </summary>
    private const string TextKeyPrefix = "Field.Sheet.";

    /// <summary>字段声明（顺序 = 界面顺序，两列按行配对）。</summary>
    public static IReadOnlyList<ParameterDefinition> Definitions { get; } =
    [
        .. RawDefinitions().Select(definition => definition with
        {
            // 显示名/说明/选项按前缀挂键；**已经显式给了键的不动**（例：项目名称在马歇尔单上叫
            // 「试验项目名称」，与弯曲的「项目名称」不是同一个词，所以单独给了键）
            DisplayNameKey = string.IsNullOrEmpty(definition.DisplayNameKey)
                ? TextKeyPrefix + definition.Key
                : definition.DisplayNameKey,
            DescriptionKey = string.IsNullOrWhiteSpace(definition.Description)
                ? string.Empty
                : string.IsNullOrEmpty(definition.DescriptionKey)
                    ? $"{TextKeyPrefix}{definition.Key}.Desc"
                    : definition.DescriptionKey,
            Options = definition.Options?
                .Select(option => option with
                {
                    LabelKey = $"{TextKeyPrefix}{definition.Key}.{option.Value}",
                })
                .ToArray(),
        }),
    ];

    /// <summary>中文兜底的原始声明（键由上面那段按约定补）。</summary>
    private static IReadOnlyList<ParameterDefinition> RawDefinitions() =>
    [
        // r1：试验单号（只读）｜毛体积密度
        Text("SheetID", "试验单号", readOnlyHint: "自动生成（只读）"),
        Number("SampleDensity", "毛体积密度", "g/cm³", group: ParameterGroup.SheetVolume),

        // r2：委托单位｜空隙率
        Text("ClientUnits", "委托单位"),
        Number("VoidRatio", "空隙率", "%", group: ParameterGroup.SheetVolume),

        // r3：监理单位｜沥青用量
        Text("SupervisionUnits", "监理单位"),
        Number("AsphaltUsage", "沥青用量", "%", group: ParameterGroup.SheetVolume),

        // r4：试验项目名称｜沥青体积百分率
        Text(ProjectNameKey, "试验项目名称 *", displayNameKey: "Field.Sheet.T0709.ProjectName"),
        Number("ContentByVolume", "沥青体积百分率", "%", group: ParameterGroup.SheetVolume),

        // r5：试验依据｜沥青饱和度
        Choice(
            SpecificationKey,
            "试验依据 *",
            SpecificationLabels,
            description: "T0709 混合料与 T0710 路面芯样；选芯样时「试验方法」只剩标准马歇尔、试件尺寸换成 φ100/φ150 芯样档"
        ),
        Number("AsphaltSaturation", "沥青饱和度", "%", group: ParameterGroup.SheetVolume),

        // r6：试验方法｜矿料间隙率
        Choice(StandardsKey, "试验方法 *", StandardsLabels),
        Number(
            "MineralAggregateVoidRatio",
            "矿料间隙率",
            "%",
            group: ParameterGroup.SheetVolume
        ),

        // r7：试件尺寸(mm)｜接样日期
        Choice(SampleSizeKey, "试件尺寸(mm) *", SampleSizeLabels),
        Date("AcceptanceDate", "接样日期"),

        // r8：沥青混合料类型｜试验时间
        Text(MixtureTypeKey, "沥青混合料类型 *"),
        Text("CreatedOn", "试验时间", readOnlyHint: "新建时填当前时间"),

        // r9：试验温度｜试验员
        // 温度按**规范值 60℃**预填（现役是空的，用户每单都要手填）；试验员留空
        Text(TemperatureKey, "试验温度(℃) *", defaultValue: "60", description: "标准马歇尔试件的养护与试验温度 60℃（规范 T 0709）"),
        Text("Tester", "试验员"),
    ];

    /// <summary>按试验依据代号取"试验方法"的可选项（界面对话框只需要文字，代号沿用现役值域）。</summary>
    public static IReadOnlyList<string> StandardsFor(int specificationId) =>
        specificationId == 1 ? [StandardsLabels[0]] : StandardsLabels;

    /// <summary>按试验依据代号取"试件尺寸"的可选项。</summary>
    public static IReadOnlyList<string> SampleSizesFor(int specificationId) =>
        specificationId == 1 ? CoreSampleSizeLabels : SampleSizeLabels;

    private static ParameterDefinition Text(
        string key,
        string displayName,
        string description = "",
        string readOnlyHint = "",
        string displayNameKey = "",
        string defaultValue = ""
    ) =>
        new(
            key,
            displayName,
            string.Empty,
            defaultValue,
            ParameterGroup.Sheet,
            ParameterKind.Text,
            Scope: ParameterScope.Computation,
            Description: string.IsNullOrEmpty(readOnlyHint) ? description : readOnlyHint,
            DisplayNameKey: displayNameKey
        );

    private static ParameterDefinition Number(
        string key,
        string displayName,
        string unit,
        string defaultValue = "",
        ParameterGroup group = ParameterGroup.Sheet,
        double? minimum = null,
        double? maximum = null
    ) =>
        new(
            key,
            displayName,
            unit,
            defaultValue,
            group,
            ParameterKind.Number,
            Minimum: minimum,
            Maximum: maximum,
            Scope: ParameterScope.Computation
        );

    private static ParameterDefinition Date(string key, string displayName) =>
        new(
            key,
            displayName,
            string.Empty,
            string.Empty,
            ParameterGroup.Sheet,
            ParameterKind.Date,
            Scope: ParameterScope.Computation
        );

    private static ParameterDefinition Choice(
        string key,
        string displayName,
        IReadOnlyList<string> labels,
        string description = ""
    ) =>
        new(
            key,
            displayName,
            string.Empty,
            "0",
            ParameterGroup.Sheet,
            ParameterKind.Choice,
            Scope: ParameterScope.Computation,
            Description: description,
            Options:
            [
                .. labels.Select(
                    (label, index) => new ParameterOption(
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        label
                    )
                ),
            ]
        );
}
