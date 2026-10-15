namespace ANX.Method.Bending;

using ANX.Control;

/// <summary>
/// **试验单信息**的字段声明（试验单编辑窗体用）。顺序**照现役窗体 `uEditBendingTestForm` 的次序**
/// （用户 2026-10-14 明确），分两组：工程与试验条件、体积指标。
///
/// 口径（用户 2026-10-14）：
/// <list type="bullet">
///   <item>「试件尺寸」只是该单的**描述性记录**（进报表）——判定仍用逐件试样信息的 b/h/跨径；</item>
///   <item>「试验温度 / 加载速率」是该单的**试验条件记录**：实际控制按单上填的加载速率走
///         （保存时把它写回参数表）；</item>
///   <item>体积指标**允许留空**；</item>
///   <item>试验规范 / 试验目的 / 试件尺寸用下拉，选项照现役。</item>
/// </list>
/// </summary>
public static class BendingSheetFields
{
    /// <summary>项目名称：现役唯一的必填项（另有"必须选试验规范"）。</summary>
    public const string ProjectNameKey = "ProjectName";

    /// <summary>试验规范：必选。</summary>
    public const string SpecificationKey = "TestSpecificationsId";

    /// <summary>试验目的：切换时按现役联动预填温度与加载速率。</summary>
    public const string ObjectiveKey = "TestObjectiveId";

    /// <summary>试件尺寸档（按公称最大粒径选；**只是该单的描述性记录**）。</summary>
    public const string SampleSizeKey = "SampleSizeId";

    /// <summary>混合料类型（现役 AsphaltMixtureType）。</summary>
    public const string MixtureTypeKey = "MixtureType";

    /// <summary>
    /// 该单的试验温度（℃）——记录在单上、进报表，**控制环节不读它**。
    /// 参数表里没有对应项：新建时按**规范要求的 15℃**（低温弯拉 −10℃）做缺省（用户 2026-10-14）。
    /// </summary>
    public const string TemperatureKey = "TestTemperature";

    /// <summary>该单的加载速率（mm/min）——保存时写回参数表，实际控制按它走。</summary>
    public const string LoadRateKey = "LoadRate";

    /// <summary>试验目的的选项：0 = 常温弯拉性能（15℃/50），1 = 低温弯拉性能（−10℃/50）。</summary>
    public static readonly string[] ObjectiveLabels = ["常温弯拉性能", "低温弯拉性能"];

    /// <summary>与 <see cref="ObjectiveLabels"/> 对应的默认温度（℃）与加载速率（mm/min）。</summary>
    public static readonly (string Temperature, string LoadRate)[] ObjectiveDefaults =
    [
        ("15", "50"),
        ("-10", "50"),
    ];

    /// <summary>试件尺寸档（描述性记录）：现役三个档，`(250×30x35)mm` 等。</summary>
    public static readonly string[] SampleSizeLabels =
    [
        "(250×30x35)mm",
        "(250×40x40)mm",
        "(250×50x50)mm",
    ];

    /// <summary>试验规范选项（现役弯曲只有一条）。</summary>
    public const string SpecificationLabel = "T 0715-2025 沥青混合料弯曲试验";

    /// <summary>
    /// 试验单字段的显示文本资源键前缀。**自成一组**（不复用 <see cref="ParameterTextKeys"/> 的
    /// <c>Field.&lt;参数键&gt;</c>）：试验单上的名字带 <c> *</c> 标必填、"加载速率"这类名字与参数表里的
    /// 同一项措辞也不同，混用键就会互相牵扯。
    /// </summary>
    private const string TextKeyPrefix = "Field.Sheet.";

    /// <summary>字段声明（顺序 = 界面顺序）。显示名 / 说明 / 选项按 <see cref="TextKeyPrefix"/> 挂键。</summary>
    public static IReadOnlyList<ParameterDefinition> Definitions { get; } =
    [
        .. RawDefinitions().Select(definition => definition with
        {
            DisplayNameKey = TextKeyPrefix + definition.Key,
            DescriptionKey = string.IsNullOrWhiteSpace(definition.Description)
                ? string.Empty
                : $"{TextKeyPrefix}{definition.Key}.Desc",
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
        // 次序照现役窗体 `uEditBendingTestForm` 的**版面**（不是 DFM 里的声明次序）：
        // 现役是一个两列网格，左列 = 工程与试验条件、右列 = 体积指标 6 项 + 接样日期 / 试验时间 / 试验员，
        // 两列**按行配对**（用户 2026-10-14 明确"控件排列按照现役软件中的次序"）。
        // 界面用两列 UniformGrid **按行填充**，所以这里按"一左一右"的顺序声明即可还原现役版面。
        //
        // 名称带 " *" = 必填（照现役 `ValidatingInput`：项目名称、试验规范、试验目的、试件尺寸、
        // 混合料类型、试验温度、加载速率）；体积指标 6 项**允许留空**（用户 2026-10-14）。

        // r1
        Text("SheetID", "试验单号", readOnlyHint: "自动生成（只读）"),
        Number("SampleDensity", "试样密度", "g/cm³", group: ParameterGroup.SheetVolume),

        // r2
        Text("ClientUnits", "委托单位"),
        Number("VoidRatio", "空隙率", "%", group: ParameterGroup.SheetVolume),

        // r3
        Text("SupervisionUnits", "监理单位"),
        Number("AsphaltUsage", "沥青用量", "%", group: ParameterGroup.SheetVolume),

        // r4
        Text(ProjectNameKey, "项目名称 *"),
        Number("ContentByVolume", "沥青体积百分比", "%", group: ParameterGroup.SheetVolume),

        // r5
        Choice(SpecificationKey, "试验规范 *", SpecificationLabel),
        Number("AsphaltSaturation", "沥青饱和度", "%", group: ParameterGroup.SheetVolume),

        // r6
        Text(MixtureTypeKey, "混合料类型 *"),
        Number(
            "MineralAggregateVoidRatio",
            "矿料间隙率",
            "%",
            group: ParameterGroup.SheetVolume
        ),

        // r7
        Choice(
            SampleSizeKey,
            "试件尺寸(mm) *",
            SampleSizeLabels,
            description: "该单的描述性记录；判定用逐件试样信息的 b/h/跨径"
        ),
        Date("AcceptanceDate", "接样日期"),

        // r8
        Choice(ObjectiveKey, "试验目的 *", ObjectiveLabels),
        Text("CreatedOn", "试验时间", readOnlyHint: "新建时填当前时间"),

        // r9
        Number(TemperatureKey, "试验温度 *", "℃", "15"),
        Text("Operator", "试验员"),

        // r10
        // 加载速率这一项**会写回参数表、实际控制按它走**，所以范围与参数表声明一致（0.1~50）：
        // 电机上限 50mm/min，超了伺服宕机（用户 2026-10-11 明确）
        Number(LoadRateKey, "加载速率 *", "mm/min", "50", minimum: 0.1, maximum: 50),
    ];

    private static ParameterDefinition Text(
        string key,
        string displayName,
        string description = "",
        string readOnlyHint = ""
    ) =>
        new(
            key,
            displayName,
            string.Empty,
            string.Empty,
            ParameterGroup.Sheet,
            ParameterKind.Text,
            Scope: ParameterScope.Computation,
            Description: string.IsNullOrEmpty(readOnlyHint) ? description : readOnlyHint
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
        string label,
        string description = ""
    ) =>
        Choice(key, displayName, [label], description);

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
