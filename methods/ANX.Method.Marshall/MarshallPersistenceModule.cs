namespace ANX.Method.Marshall;

using ANX.Core.Methods;

/// <summary>
/// T 0709（马歇尔稳定度）的三张表：主表 / 明细表 / 模板表。
///
/// **列名逐列照现役**（用户 2026-10-16 定的长期口规则：后续方法的字段名必须与现役一致，
/// 例外只有表名与纯新增列）—— 现役列名与列序取自现役真库
/// `Bins\Data\AsphaltMechanicalTesting.db` 的 `AsphaltMarshallStability*` 三表：
/// 主表 37 列 / 明细 32 列 / 模板 14 列。保留下来的、**有意的**差异只有：
/// <list type="bullet">
///   <item>表名用 C# 侧命名：`MarshallTestMain` / `MarshallTestDetail` / `MarshallTestTemplate`
///         （不与现役库互通是既定红线；现役叫 `AsphaltMarshallStability*`）；</item>
///   <item>数值列用真实类型（现役用 `VARCHAR(10)` 冒充数字，靠 `NumField` 兜空串，这条路不走）；</item>
///   <item>模板表主键叫 `TemplateID`（现役沿用一个 `SheetID`），与弯曲同款；</item>
///   <item>不加现役的 `DetailChart` / `DetailReport` 两张附表：曲线按 C# 侧既有口径
///         序列化进 `ChartData`（弯曲同款），报表填过的值进明细的 `ReportData`。</item>
/// </list>
/// 每列都带中文含义注释，`SchemaBuilder` 会把注释写进建表 SQL。
/// </summary>
public sealed class MarshallPersistenceModule : IMethodPersistenceModule
{
    public const string MainTableName = "MarshallTestMain";

    public const string DetailTableName = "MarshallTestDetail";

    public const string TemplateTableName = "MarshallTestTemplate";

    public string MethodCode => "T0709";

    public TableDefinition MainTable { get; } = new(
        MainTableName,
        ["SheetID"],
        [
            // ── 标识与排序 ──
            new ColumnDefinition("SheetID", ColumnType.Text, true, "试验单号（主键，形如 MS20261016001）"),
            new ColumnDefinition("UUID", ColumnType.Text, Comment: "记录唯一标识（GUID）；现役 UUID"),
            new ColumnDefinition("SheetIndex", ColumnType.Integer, Comment: "试验单排序索引；现役 SheetIndex"),

            // ── 工程信息（照现役主表列序）──
            new ColumnDefinition("ClientUnits", ColumnType.Text, Comment: "委托单位"),
            new ColumnDefinition("SupervisionUnits", ColumnType.Text, Comment: "监理单位"),
            new ColumnDefinition("ProjectName", ColumnType.Text, Comment: "工程（项目）名称"),
            new ColumnDefinition("TestSpecificationsID", ColumnType.Integer, Comment: "试验依据（执行标准）代号；现役 TestSpecificationsID"),
            new ColumnDefinition("TestSpecificationsDesc", ColumnType.Text, Comment: "试验依据描述"),
            new ColumnDefinition("TestStandardsID", ColumnType.Integer, Comment: "试验标准代号（马歇尔比弯曲多这一对：试验依据 vs 试验标准）"),
            new ColumnDefinition("TestStandardsDesc", ColumnType.Text, Comment: "试验标准描述"),
            new ColumnDefinition("SampleSizeID", ColumnType.Integer, Comment: "试件尺寸代号（φ101.6×63.5 / φ152.4×95.3）"),
            new ColumnDefinition("SampleSizeDesc", ColumnType.Text, Comment: "试件尺寸描述"),

            // ── 混合料与体积指标 ──
            new ColumnDefinition("AsphaltMixtureType", ColumnType.Text, Comment: "沥青混合料类型；现役 AsphaltMixtureType"),
            new ColumnDefinition("SampleDensity", ColumnType.Real, Comment: "试件毛体积相对密度；现役用文本"),
            new ColumnDefinition("VoidRatio", ColumnType.Real, Comment: "空隙率（%）"),
            new ColumnDefinition("AsphaltUsage", ColumnType.Real, Comment: "沥青用量（%）"),
            new ColumnDefinition("ContentByVolume", ColumnType.Real, Comment: "沥青体积百分率（%）"),
            new ColumnDefinition("MineralAggregateVoidRatio", ColumnType.Real, Comment: "矿料间隙率 VMA（%）"),
            new ColumnDefinition("AsphaltSaturation", ColumnType.Real, Comment: "沥青饱和度 VFA（%）"),

            // ── 两组统计（现役把统计列直接放在主表上）──
            new ColumnDefinition("Group1AvgFlowValue", ColumnType.Real, Comment: "第一组流值平均值（0.1mm）"),
            new ColumnDefinition("Group2AvgFlowValue", ColumnType.Real, Comment: "第二组流值平均值（0.1mm）"),
            new ColumnDefinition("Group1AvgStability", ColumnType.Real, Comment: "第一组稳定度平均值（kN）"),
            new ColumnDefinition("Group2AvgStability", ColumnType.Real, Comment: "第二组稳定度平均值（kN）"),
            new ColumnDefinition("Group1Stability_Std_Dev", ColumnType.Real, Comment: "第一组稳定度标准差"),
            new ColumnDefinition("Group2Stability_Std_Dev", ColumnType.Real, Comment: "第二组稳定度标准差"),
            new ColumnDefinition("Group1Stability_Cv_Percent", ColumnType.Real, Comment: "第一组稳定度变异系数（%）"),
            new ColumnDefinition("Group2Stability_Cv_Percent", ColumnType.Real, Comment: "第二组稳定度变异系数（%）"),
            new ColumnDefinition("Group1Flow_Std_Dev", ColumnType.Real, Comment: "第一组流值标准差"),
            new ColumnDefinition("Group2Flow_Std_Dev", ColumnType.Real, Comment: "第二组流值标准差"),
            new ColumnDefinition("Group1Flow_Cv_Percent", ColumnType.Real, Comment: "第一组流值变异系数（%）"),
            new ColumnDefinition("Group2Flow_Cv_Percent", ColumnType.Real, Comment: "第二组流值变异系数（%）"),
            new ColumnDefinition("StabilityRatio", ColumnType.Real, Comment: "残留稳定度比（第二组稳定度 / 第一组，%）"),

            // ── 试验条件与收尾 ──
            new ColumnDefinition("TestTemperatureDesc", ColumnType.Text, Comment: "试验温度（℃）；现役列名 TestTemperatureDesc"),
            new ColumnDefinition("Tester", ColumnType.Text, Comment: "试验员；现役 Tester（弯曲侧 C# 叫 Operator）"),
            new ColumnDefinition("AcceptanceDate", ColumnType.Text, Comment: "接样日期"),
            // 现役主表这两列叫 CreateDate；C# 侧沿用弯曲的命名 CreatedOn / CompletedOn
            //（框架 TestSheetService 建单与"标记完成"就认这两个名字，且带 IsDeclared 兜底）
            new ColumnDefinition("CreatedOn", ColumnType.Text, Comment: "创建时间（yyyy-MM-dd HH:mm:ss）；现役 CreateDate"),
            new ColumnDefinition("CompletedOn", ColumnType.Text, Comment: "完成时间（yyyy-MM-dd HH:mm:ss）；C# 侧新增（现役没有这一列）"),
            new ColumnDefinition("AvailabilityCalculated", ColumnType.Integer, Comment: "是否已算过有效性/统计：1=算过"),
        ],
        "T 0709 马歇尔稳定度主表 —— 一试单一行的头信息 + 两组统计列（列名照现役）"
    );

    public TableDefinition DetailTable { get; } = new(
        DetailTableName,
        ["SheetID", "SampleID"],
        [
            // ── 标识与分组 ──
            new ColumnDefinition("SheetID", ColumnType.Text, Comment: "所属试验单号；现役 SheetID"),
            new ColumnDefinition("SampleID", ColumnType.Text, Comment: "试件号（单内顺序号）；现役 SampleID"),
            new ColumnDefinition("UUID", ColumnType.Text, Comment: "记录唯一标识（GUID），沿用所属单的 UUID；现役 UUID"),
            new ColumnDefinition("SampleDesc", ColumnType.Text, Comment: "试件描述；现役 SampleDesc"),
            new ColumnDefinition("GroupID", ColumnType.Integer, Comment: "组别代号（1=第一组 / 2=第二组；水中马歇尔即第二组）"),
            new ColumnDefinition("GroupDesc", ColumnType.Text, Comment: "组别描述"),
            new ColumnDefinition("SheetIndex", ColumnType.Integer, Comment: "单内排序索引；现役 SheetIndex"),

            // ── 试件与试验条件（逐件）──
            new ColumnDefinition("TestTemperatureDesc", ColumnType.Text, Comment: "试验温度（℃）；现役 TestTemperatureDesc"),
            new ColumnDefinition("SampleInitialHeight", ColumnType.Real, Comment: "试件初始高度（mm），规范 63.5±1.3；修正系数按它查"),
            new ColumnDefinition("SampleInitialDiameter", ColumnType.Real, Comment: "试件初始直径（mm），规范 101.6±0.25"),
            new ColumnDefinition("StrainRate", ColumnType.Real, Comment: "加载速率（mm/min），规范 50±5"),

            // ── 判定结果（列名照现役）──
            new ColumnDefinition("CorrectionFactor", ColumnType.Real, Comment: "试件高度修正系数 K（按 h/D 查表 + 双线性插值）"),
            new ColumnDefinition("LoadPeakValue", ColumnType.Real, Comment: "峰值荷载（kN）"),
            new ColumnDefinition("DeformationPeakValue", ColumnType.Real, Comment: "峰值荷载对应的变形（mm）"),
            new ColumnDefinition("MSRaw", ColumnType.Real, Comment: "稳定度（未修正，kN）"),
            new ColumnDefinition("MSCorrected", ColumnType.Real, Comment: "修正后稳定度 = MSRaw × CorrectionFactor（kN）"),
            new ColumnDefinition("FlowValue", ColumnType.Real, Comment: "流值（0.1mm 为单位，1mm = 10）"),
            new ColumnDefinition("MarshallModulus", ColumnType.Real, Comment: "马歇尔模数 T = 稳定度 / 流值（kN/mm）"),

            // ── 拟合与曲线（列名照现役）──
            new ColumnDefinition("OPrime", ColumnType.Real, Comment: "修正原点 O′ 的横坐标；现役 OPrime"),
            new ColumnDefinition("KFactor", ColumnType.Real, Comment: "修正直线斜率；现役 KFactor"),
            new ColumnDefinition("PeakX", ColumnType.Real, Comment: "峰值点横坐标；现役 PeakX"),
            new ColumnDefinition("PeakY", ColumnType.Real, Comment: "峰值点纵坐标；现役 PeakY"),
            new ColumnDefinition("TangentK", ColumnType.Real, Comment: "修正直线斜率（与弯曲同名的拟合结果列）；现役 TangentK"),
            new ColumnDefinition("TangentB", ColumnType.Real, Comment: "修正直线截距；现役 TangentB"),
            new ColumnDefinition("IsObvious", ColumnType.Integer, Comment: "峰值是否明显：1=明显（直接取峰值）/ 0=走修正直线平移 1.5mm 求交点"),
            new ColumnDefinition("IsSuspiciousNoPeak", ColumnType.Integer, Comment: "疑似无峰值（异常数据标记）：1=是"),
            new ColumnDefinition("EndPointIndex", ColumnType.Integer, Comment: "曲线有效段终点在采样序列中的索引；现役 EndPointIndex"),
            new ColumnDefinition("ChartData", ColumnType.Text, Comment: "荷载-变形曲线序列化（紧凑 JSON，保留 3 位小数）"),
            new ColumnDefinition("ReportData", ColumnType.Text, Comment: "报表填过的值（可追溯）；现役 ReportData"),

            // ── 收尾 ──
            new ColumnDefinition("Effective", ColumnType.Integer, Comment: "是否有效：1=计入组统计，0=被剔除；现役 Effective"),
            new ColumnDefinition("TestStatus", ColumnType.Integer, Comment: "试件试验状态：0=未做 / 1=试验中 / 2=完成 / 3=失败（统计只取 2）；现役 TestStatus"),
            new ColumnDefinition("CreateDate", ColumnType.Text, Comment: "创建时间（yyyy-MM-dd HH:mm:ss）；现役 CreateDate"),
        ],
        "T 0709 马歇尔稳定度明细表 —— 一试件一行（分组 / 尺寸 / 判定值 / 拟合 / 曲线）"
    );

    public TableDefinition TemplateTable { get; } = new(
        TemplateTableName,
        ["TemplateID"],
        [
            // 现役模板表 14 列，逐列对应（主键 现役 SheetID → C# TemplateID，与弯曲同款）
            new ColumnDefinition("TemplateID", ColumnType.Text, true, "模板标识（主键）；现役沿用一个 SheetID 作主键"),
            new ColumnDefinition("SheetIndex", ColumnType.Integer, Comment: "排序索引；现役 SheetIndex"),
            new ColumnDefinition("ClientUnits", ColumnType.Text, Comment: "委托单位"),
            new ColumnDefinition("SupervisionUnits", ColumnType.Text, Comment: "监理单位"),
            new ColumnDefinition("ProjectName", ColumnType.Text, Comment: "工程（项目）名称"),
            new ColumnDefinition("TestSpecificationsID", ColumnType.Integer, Comment: "试验依据代号"),
            new ColumnDefinition("TestSpecificationsDesc", ColumnType.Text, Comment: "试验依据描述"),
            new ColumnDefinition("TestStandardsID", ColumnType.Integer, Comment: "试验标准代号"),
            new ColumnDefinition("TestStandardsDesc", ColumnType.Text, Comment: "试验标准描述"),
            new ColumnDefinition("SampleSizeID", ColumnType.Integer, Comment: "试件尺寸代号"),
            new ColumnDefinition("SampleSizeDesc", ColumnType.Text, Comment: "试件尺寸描述"),
            new ColumnDefinition("TestTemperatureDesc", ColumnType.Text, Comment: "试验温度（℃）"),
            new ColumnDefinition("Tester", ColumnType.Text, Comment: "试验员"),
            new ColumnDefinition("CreateDate", ColumnType.Text, Comment: "创建时间"),
        ],
        "T 0709 马歇尔稳定度模板表 —— 主表去掉两组统计与结果列，用于参数预设 / 继承上次试验信息"
    );
}
