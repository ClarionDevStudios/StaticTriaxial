namespace ANX.Method.Bending;

using ANX.Core.Methods;

/// <summary>
/// T 0715 的三张表：主表（一试单一行的头信息 + 组统计列）、明细表（一试件一行）、
/// 模板表（主表去掉组统计列与运行结果列，用于参数预设、继承上次试验信息）。
///
/// 结构按现役 `SQL\T0715-2025_BendingTest.sql` **逐列补齐**（现役主表 37 列 / 明细 27 列 / 模板 31 列），
/// 保留下来的差异：
/// <list type="bullet">
///   <item>表名沿用 C# 侧命名（不与现役库兼容是既定红线），不叫 `AsphaltBendingTest*`；</item>
///   <item>数值列一律真实类型，不用 `VARCHAR(10)` 冒充数字（现役靠 `NumField` 兜空串，这条路不走）；</item>
///   <item>`SpanLength` 提到主表（现役放在明细里）、模板主键叫 `TemplateID`（现役沿用一个 `SheetID`）；</item>
///   <item>现役明细的 `HasStraightLine` / `IsObvious` **故意不搬**：它们服务于已抛弃的
///         "修正直线平移 1.5mm 求交点"路径；</item>
///   <item>新增 `EffectiveCount` / `ExcludedCount` / `NeedsSupplement` / `InvalidReason`，
///         让"剔除后不足 3 个需补做"这类结论能落库，而不是只停在界面上。</item>
/// </list>
/// 每列都带中文含义注释（含单位与取值口径），`SchemaBuilder` 会把注释写进建表 SQL，
/// 生成结果见 `docs\对照\建表SQL.sql`（由 `ANX.Tools.SchemaDump` 生成）。
/// </summary>
public sealed class BendingPersistenceModule : IMethodPersistenceModule
{
    public const string MainTableName = "BendingTestMain";

    public const string DetailTableName = "BendingTestDetail";

    public const string TemplateTableName = "BendingTestMainTemplate";

    public string MethodCode => "T0715";

    public TableDefinition MainTable { get; } = new(
        MainTableName,
        ["SheetID"],
        [
            // ── 标识与排序 ──
            new ColumnDefinition("SheetID", ColumnType.Text, true, "试验单号（主键，形如 WT20261011001）"),
            new ColumnDefinition("UUID", ColumnType.Text, Comment: "记录唯一标识（GUID）；现役 UUID"),
            new ColumnDefinition("SheetIndex", ColumnType.Integer, Comment: "试验单排序索引；现役 SheetIndex"),

            // ── 工程信息（规范 7 的报告抬头；现役主表头信息）──
            new ColumnDefinition("ProjectName", ColumnType.Text, Comment: "工程（项目）名称"),
            new ColumnDefinition("ClientUnits", ColumnType.Text, Comment: "委托单位"),
            new ColumnDefinition("SupervisionUnits", ColumnType.Text, Comment: "监理单位"),
            new ColumnDefinition("TestSpecificationsId", ColumnType.Integer, Comment: "试验依据（执行标准）代号；现役 TestSpecificationsID"),
            new ColumnDefinition("TestSpecificationsDesc", ColumnType.Text, Comment: "试验依据描述，如 JTG 3410-2025 T 0715-2025"),
            new ColumnDefinition("AcceptanceDate", ColumnType.Text, Comment: "接样日期（yyyy-MM-dd）"),

            // ── 试验条件（规范 1.2 / 4.1 / 4.5）──
            new ColumnDefinition("TestObjectiveId", ColumnType.Integer, Comment: "试验目的代号：0=常温弯拉性能（15℃），1=低温弯拉性能（−10℃）"),
            new ColumnDefinition("TestObjective", ColumnType.Text, Comment: "试验目的描述（常温弯拉性能 / 低温弯拉性能）"),
            new ColumnDefinition("TestTemperature", ColumnType.Real, Comment: "试验温度（℃）；现役用文本 TestTemperatureDesc，这里用真实类型"),
            new ColumnDefinition("LoadRate", ColumnType.Real, Comment: "加载速率（mm/min），规范 50±5"),
            new ColumnDefinition("SpanLength", ColumnType.Real, Comment: "支点间距（跨径）L（mm），规范 200±0.5；现役放在明细表"),

            // ── 混合料与试件信息（规范 3.4 / 7）──
            new ColumnDefinition("MixtureType", ColumnType.Text, Comment: "沥青混合料类型；现役 AsphaltMixtureType"),
            new ColumnDefinition("SampleSizeId", ColumnType.Integer, Comment: "试件尺寸代号（按公称最大粒径选取）；现役 SampleSizeID"),
            new ColumnDefinition("SampleSizeDesc", ColumnType.Text, Comment: "试件尺寸描述，如 250×30×35（长×宽×高，mm）"),
            new ColumnDefinition("FormingMethodId", ColumnType.Integer, Comment: "成型方法代号；现役 FormingMethodID"),
            new ColumnDefinition("FormingMethod", ColumnType.Text, Comment: "成型方法描述（轮碾法 / 静压法 等）"),
            new ColumnDefinition("SampleDensity", ColumnType.Real, Comment: "试件密度（g/cm³）"),
            new ColumnDefinition("VoidRatio", ColumnType.Real, Comment: "空隙率 VV（%）"),
            new ColumnDefinition("AsphaltUsage", ColumnType.Real, Comment: "沥青用量（%）"),
            new ColumnDefinition("ContentByVolume", ColumnType.Real, Comment: "沥青体积百分率（%）"),
            new ColumnDefinition("MineralAggregateVoidRatio", ColumnType.Real, Comment: "矿料间隙率 VMA（%）"),
            new ColumnDefinition("AsphaltSaturation", ColumnType.Real, Comment: "沥青饱和度 VFA（%）"),

            // ── 统计口径（现役可配置；新工程把这些从算法里挪到库里）──
            new ColumnDefinition("StdDevMethodId", ColumnType.Integer, Comment: "标准差计算方法代号；现役 StdDevMethodID"),
            new ColumnDefinition("StdDevMethodDesc", ColumnType.Text, Comment: "标准差计算方法描述（样本 / 总体）"),
            new ColumnDefinition("OutlierDetectionMethodId", ColumnType.Integer, Comment: "超差剔除依据代号：0=最大弯拉应变（规范默认），1=弯拉强度"),
            new ColumnDefinition("OutlierDetectionMethodDesc", ColumnType.Text, Comment: "超差剔除依据描述"),
            new ColumnDefinition("AllowedError", ColumnType.Real, Comment: "最大弯拉应变允许误差（με），规范默认 400；现役存文本 '400'"),

            // ── 组统计结果（规范 5.5；强度 0.01MPa、应变 1με）──
            new ColumnDefinition("FlexuralStrengthAvg", ColumnType.Real, Comment: "弯拉强度组平均值 R_B（MPa，精确至 0.01）"),
            new ColumnDefinition("FlexuralStrengthStdDev", ColumnType.Real, Comment: "弯拉强度组标准差（MPa）；现役 FlexuralStrength_Std_Dev"),
            new ColumnDefinition("FlexuralStrengthCvPercent", ColumnType.Real, Comment: "弯拉强度变异系数 Cv（%）；现役 FlexuralStrength_Cv_Percent"),
            new ColumnDefinition("MaxBendingStrainAvg", ColumnType.Real, Comment: "最大弯拉应变组平均值 ε_B（με，精确至 1）"),
            new ColumnDefinition("MaxBendingStrainStdDev", ColumnType.Real, Comment: "最大弯拉应变组标准差（με）；现役 MaxBendingTensileStrain_Std_Dev"),
            new ColumnDefinition("MaxBendingStrainCvPercent", ColumnType.Real, Comment: "最大弯拉应变变异系数 Cv（%）；现役 MaxBendingTensileStrain_Cv_Percent"),

            // ── 生命周期与有效性 ──
            new ColumnDefinition("Operator", ColumnType.Text, Comment: "试验员；现役 Tester"),
            new ColumnDefinition("CreatedOn", ColumnType.Text, Comment: "创建时间（yyyy-MM-dd HH:mm:ss）；现役 CreateDate"),
            new ColumnDefinition("CompletedOn", ColumnType.Text, Comment: "完成时间（yyyy-MM-dd HH:mm:ss）"),
            new ColumnDefinition("EffectiveCount", ColumnType.Integer, Comment: "有效试件数（剔除后计入组统计的件数）"),
            new ColumnDefinition("ExcludedCount", ColumnType.Integer, Comment: "被剔除的试件数（规范 5.6 超差剔除）"),
            new ColumnDefinition("NeedsSupplement", ColumnType.Integer, Comment: "是否需补做：1=剔除后不足 3 个需补做，0=不需要（规范 3.4）"),
        ],
        "T 0715 试验单主表 —— 一试单一行的头信息（工程信息 / 试验条件 / 统计口径）+ 组统计结果"
    );

    public TableDefinition DetailTable { get; } = new(
        DetailTableName,
        ["SheetID", "SampleID"],
        [
            // ── 标识 ──
            new ColumnDefinition("SheetID", ColumnType.Text, true, "所属试验单号；与 SampleID 一起构成明细主键"),
            new ColumnDefinition("SampleID", ColumnType.Text, true, "试件编号，形如 S001（现役同格式，单内顺序号）；与 SheetID 一起构成明细主键"),
            new ColumnDefinition("UUID", ColumnType.Text, Comment: "记录唯一标识（GUID）；现役 UUID"),
            new ColumnDefinition("SampleDesc", ColumnType.Text, Comment: "试件描述；现役 SampleDesc"),
            new ColumnDefinition("SheetIndex", ColumnType.Integer, Comment: "试件排序索引；现役 SheetIndex"),

            // ── 试件尺寸与试验条件 ──
            new ColumnDefinition("SampleLength", ColumnType.Real, Comment: "试件长度（mm），规范 250"),
            new ColumnDefinition("SampleWidth", ColumnType.Real, Comment: "试件宽度 b（mm），规范 30/40/50（按公称最大粒径）"),
            new ColumnDefinition("SampleHeight", ColumnType.Real, Comment: "试件高度 h（mm），规范 35/40/50（按公称最大粒径）"),
            new ColumnDefinition("SpanLength", ColumnType.Real, Comment: "支点间距（跨径）L（mm），规范 200；现役明细列"),
            new ColumnDefinition("StrainRate", ColumnType.Real, Comment: "加载速率（mm/min），默认 50；现役 StrainRate"),
            new ColumnDefinition("TestTemperature", ColumnType.Real, Comment: "该试件的试验温度（℃），一般与主表一致；现役 TestTemperatureDesc"),

            // ── 单件判定值（规范 5.1~5.4）──
            new ColumnDefinition("PeakLoadValue", ColumnType.Real, Comment: "破坏荷载 P_B（kN）；现役 LoadPeakValue"),
            new ColumnDefinition("PeakDeflection", ColumnType.Real, Comment: "跨中挠度峰值 d（mm）；现役 DeformationPeakValue"),
            new ColumnDefinition("OriginCorrection", ColumnType.Real, Comment: "原点修正量 O′（mm）；现役 OPrime"),
            new ColumnDefinition("CorrectedDeflection", ColumnType.Real, Comment: "修正后跨中挠度（mm）；现役 DeformationCorrected"),
            new ColumnDefinition("TangentK", ColumnType.Real, Comment: "修正直线斜率（荷载-挠度曲线直线段）"),
            new ColumnDefinition("TangentB", ColumnType.Real, Comment: "修正直线截距"),
            new ColumnDefinition("FlexuralStrength", ColumnType.Real, Comment: "弯拉强度 R_B = 3·L·P_B / (2·b·h²)（MPa，精确至 0.01）"),
            new ColumnDefinition("MaxBendingTensileStrain", ColumnType.Real, Comment: "最大弯拉应变 ε_B = 6·h·d / L²（με，精确至 0.1）"),

            // ── 有效性与曲线 ──
            new ColumnDefinition("IsEffective", ColumnType.Integer, Comment: "是否有效：1=计入组统计，0=被剔除；现役 Effective"),
            new ColumnDefinition("InvalidReason", ColumnType.Text, Comment: "无效原因（超出允许误差 / 试验异常 等）"),
            new ColumnDefinition("EndPointIndex", ColumnType.Integer, Comment: "曲线有效段终点在采样序列中的索引；现役 EndPointIndex"),
            new ColumnDefinition("TestStatus", ColumnType.Integer, Comment: "试件试验状态：0=未做（其余取值待确认）；现役 TestStatus"),
            new ColumnDefinition("ChartData", ColumnType.Text, Comment: "荷载-跨中挠度曲线序列化（紧凑 JSON，保留 3 位小数）"),
            new ColumnDefinition("CreateDate", ColumnType.Text, Comment: "创建时间（yyyy-MM-dd HH:mm:ss）"),
            new ColumnDefinition("Remark", ColumnType.Text, Comment: "备注"),
        ],
        "T 0715 试件明细表 —— 一试件一行（尺寸 / 单件判定值 / 曲线序列化 / 有效性）"
    );

    public TableDefinition TemplateTable { get; } = new(
        TemplateTableName,
        ["TemplateID"],
        [
            // ── 标识 ──
            new ColumnDefinition("TemplateID", ColumnType.Text, true, "模板标识（主键）；现役沿用一个 SheetID 作主键"),
            new ColumnDefinition("UUID", ColumnType.Text, Comment: "记录唯一标识（GUID）；现役 UUID"),
            new ColumnDefinition("SheetIndex", ColumnType.Integer, Comment: "排序索引；现役 SheetIndex"),

            // ── 工程信息 ──
            new ColumnDefinition("ProjectName", ColumnType.Text, Comment: "工程（项目）名称"),
            new ColumnDefinition("ClientUnits", ColumnType.Text, Comment: "委托单位"),
            new ColumnDefinition("SupervisionUnits", ColumnType.Text, Comment: "监理单位"),
            new ColumnDefinition("TestSpecificationsId", ColumnType.Integer, Comment: "试验依据（执行标准）代号"),
            new ColumnDefinition("TestSpecificationsDesc", ColumnType.Text, Comment: "试验依据描述"),
            new ColumnDefinition("AcceptanceDate", ColumnType.Text, Comment: "接样日期（yyyy-MM-dd）"),

            // ── 试验条件 ──
            new ColumnDefinition("TestObjectiveId", ColumnType.Integer, Comment: "试验目的代号：0=常温，1=低温"),
            new ColumnDefinition("TestObjective", ColumnType.Text, Comment: "试验目的描述"),
            new ColumnDefinition("TestTemperature", ColumnType.Real, Comment: "试验温度（℃）"),
            new ColumnDefinition("LoadRate", ColumnType.Real, Comment: "加载速率（mm/min）"),
            new ColumnDefinition("SpanLength", ColumnType.Real, Comment: "支点间距（跨径）L（mm）"),

            // ── 混合料与试件信息 ──
            new ColumnDefinition("MixtureType", ColumnType.Text, Comment: "沥青混合料类型"),
            new ColumnDefinition("SampleSizeId", ColumnType.Integer, Comment: "试件尺寸代号"),
            new ColumnDefinition("SampleSizeDesc", ColumnType.Text, Comment: "试件尺寸描述，如 250×30×35"),
            new ColumnDefinition("FormingMethodId", ColumnType.Integer, Comment: "成型方法代号"),
            new ColumnDefinition("FormingMethod", ColumnType.Text, Comment: "成型方法描述"),
            new ColumnDefinition("SampleDensity", ColumnType.Real, Comment: "试件密度（g/cm³）"),
            new ColumnDefinition("VoidRatio", ColumnType.Real, Comment: "空隙率 VV（%）"),
            new ColumnDefinition("AsphaltUsage", ColumnType.Real, Comment: "沥青用量（%）"),
            new ColumnDefinition("ContentByVolume", ColumnType.Real, Comment: "沥青体积百分率（%）"),
            new ColumnDefinition("MineralAggregateVoidRatio", ColumnType.Real, Comment: "矿料间隙率 VMA（%）"),
            new ColumnDefinition("AsphaltSaturation", ColumnType.Real, Comment: "沥青饱和度 VFA（%）"),

            // ── 统计口径 ──
            new ColumnDefinition("StdDevMethodId", ColumnType.Integer, Comment: "标准差计算方法代号"),
            new ColumnDefinition("StdDevMethodDesc", ColumnType.Text, Comment: "标准差计算方法描述"),
            new ColumnDefinition("OutlierDetectionMethodId", ColumnType.Integer, Comment: "超差剔除依据代号：0=最大弯拉应变，1=弯拉强度"),
            new ColumnDefinition("OutlierDetectionMethodDesc", ColumnType.Text, Comment: "超差剔除依据描述"),
            new ColumnDefinition("AllowedError", ColumnType.Real, Comment: "最大弯拉应变允许误差（με），默认 400"),

            // ── 人员与时间 ──
            new ColumnDefinition("Operator", ColumnType.Text, Comment: "试验员；现役 Tester"),
            new ColumnDefinition("CreateDate", ColumnType.Text, Comment: "创建时间（yyyy-MM-dd HH:mm:ss）"),
        ],
        "T 0715 参数模板表 —— 主表去掉组统计列与运行结果列，用作参数预设、继承上次试验信息"
    );
}
