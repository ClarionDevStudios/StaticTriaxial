namespace STS.Method.ShearStrength;

using STS.Core.Methods;

/// <summary>
/// 抗剪强度（静三轴法，T 1236—2026）的持久化模块：方法三表（Main / Detail / Template）。
///
/// 规范依据：JTG 3442—2026《公路工程无结合料粒料试验规程》T 1236—2026
/// （用户 2026-10-16 提供的手册扫描件）。表结构 v1 覆盖"报告 6.1~6.8"所需信息；
/// 与现役土工三轴库的差异（无饱和 / 固结 / 孔压那一套表与字段）见 `docs\对照\T1236-数据表设计.md`。
///
/// 框架硬约定（缺一列运行时就报错）：
/// - 主表：`SheetID`（主键）、`CreatedOn`（试验日期/建单时间）、`CompletedOn`（整单收尾时间）、
///   `ProjectName`（历史列表与查询用）、`UUID`（云端去重；明细沿用同一枚）；
/// - 明细：`SheetID` + `SampleID`（复合主键）、`SheetIndex`（试件号里的序号）、`TestStatus`（0 未做 / 1 完成）、
///   `UUID`、`CreateDate`（明细创建时间）、`ChartData`（曲线 JSON）。
/// </summary>
public sealed class ShearStrengthPersistenceModule : IMethodPersistenceModule
{
    /// <summary>方法代号（骨架沿用；是否改用规范条号 `T1236` 待用户确认——改了要连带测试与资源键一起动）。</summary>
    public const string MethodCodeValue = "ShearStrength";

    public const string MainTableName = "ShearStrengthTestMain";

    public const string DetailTableName = "ShearStrengthTestDetail";

    public const string TemplateTableName = "ShearStrengthTestTemplate";

    public string MethodCode => MethodCodeValue;

    /// <summary>
    /// 试验单主表：一张单 = 一次委托（含 3 级围压 × 每级 3 件）。
    /// 前半是框架列 + 报告 6.1~6.6 的委托/样品/仪器/成型信息，
    /// 后半是收尾计算产物（c、φ、r、分级结果 JSON）与"必要时"的 SSR 输入输出。
    /// </summary>
    public TableDefinition MainTable { get; } =
        new(
            MainTableName,
            ["SheetID"],
            [
                new("SheetID", ColumnType.Text, IsRequired: true, Comment: "试验单号（主键，自动生成）"),
                new("CreatedOn", ColumnType.Text, Comment: "试验日期（建单时间，报告 6.4；yyyy-MM-dd HH:mm:ss）"),
                new("CompletedOn", ColumnType.Text, Comment: "整单收尾时间（全部试件完成后由「抗剪强度参数」写入；不代表结论定稿）"),
                new("ProjectName", ColumnType.Text, Comment: "试验项目名称（报告 6.1；历史列表与查询用）"),
                new("UUID", ColumnType.Text, Comment: "云端去重用 UUID（明细沿用同一枚）"),
                new("SampleNumber", ColumnType.Text, Comment: "样品编号（报告 6.2）"),
                new("SampleName", ColumnType.Text, Comment: "样品名称（报告 6.2；如「级配碎石」）"),
                new("SampleOrigin", ColumnType.Text, Comment: "产地（报告 6.2）"),
                new("SampleSpecification", ColumnType.Text, Comment: "规格（报告 6.2；如「0~31.5mm」）"),
                new("SampleReceiptDate", ColumnType.Text, Comment: "接样日期（报告 6.3；yyyy-MM-dd）"),
                new("SampleDescription", ColumnType.Text, Comment: "样品描述（报告 6.3，自由文本）"),
                new("SampleReductionMethod", ColumnType.Text, Comment: "缩分方法（报告 6.4；模板可复用）"),
                new("MachineNameAndID", ColumnType.Text, Comment: "主要仪器设备名称、型号及编号（报告 6.5；模板可复用）"),
                new("MoldingMethod", ColumnType.Text, Comment: "成型方法（报告 6.6；按本规程 T 1227，模板可复用）"),
                new("MoldingParameters", ColumnType.Text, Comment: "成型主要参数（报告 6.6；如分层数 / 击实功 / 目标含水率，模板可复用）"),
                new("TestPerson", ColumnType.Text, Comment: "试验员（模板可复用）"),
                new("CalculatePerson", ColumnType.Text, Comment: "计算人（模板可复用）"),
                new("VerificationPerson", ColumnType.Text, Comment: "校核人（模板可复用）"),
                new("CohesionKPa", ColumnType.Real, Comment: "黏聚力 c（kPa；式 T 1236-5，收尾计算写入）"),
                new("FrictionAngleDeg", ColumnType.Real, Comment: "内摩擦角 φ（°；式 T 1236-4，收尾计算写入）"),
                new("EnvelopeCorrelation", ColumnType.Real, Comment: "相关系数 r（式 T 1236-8；规范要求 ≥0.99）"),
                new("EnvelopeReliable", ColumnType.Integer, Comment: "包络线可信标志（0/1：r ≥ 0.99 且斜率可解；供界面提示）"),
                new("LevelResultsJson", ColumnType.Text, Comment: "分级结果 JSON：[{sigma3, meanShearStrength, count}, …]（围压级数可扩展，收尾写入）"),
                new("PavementSigma1KPa", ColumnType.Real, Comment: "「必要时」SSR 输入：路面应力条件 σ1（kPa）"),
                new("PavementSigma3KPa", ColumnType.Real, Comment: "「必要时」SSR 输入：路面应力条件 σ3（kPa）"),
                new("Ssr", ColumnType.Real, Comment: "抗剪强度比 SSR（式 T 1236-11；未计算留空）"),
            ],
            Comment: "抗剪强度（静三轴）试验单主表"
        );

    /// <summary>
    /// 试件明细表：一件一行。前半是框架列，其后是试件尺寸 / 质量湿度（规范 4.1.3、4.7、6.7）、
    /// 试验结果（Pmax、破坏应变、σ1max）与"15% 应变规则"标记。
    /// 曲线按"轴向变形 Δh（mm）- 轴向荷载（kN）"存 <c>ChartData</c>。
    /// </summary>
    public TableDefinition DetailTable { get; } =
        new(
            DetailTableName,
            ["SheetID", "SampleID"],
            [
                new("SheetID", ColumnType.Text, IsRequired: true, Comment: "所属试验单号（复合主键 1/2）"),
                new("SampleID", ColumnType.Text, IsRequired: true, Comment: "试件号（复合主键 2/2，形如 S001）"),
                new("SheetIndex", ColumnType.Integer, Comment: "试件序号（试件号里的序号，现役口径，写入时自动回填）"),
                new("TestStatus", ColumnType.Integer, Comment: "试验状态（0 未做 / 1 完成）"),
                new("UUID", ColumnType.Text, Comment: "云端去重用 UUID（与本单主表一致）"),
                new("CreateDate", ColumnType.Text, Comment: "明细创建时间（框架写入，yyyy-MM-dd HH:mm:ss）"),
                new("ChartData", ColumnType.Text, Comment: "曲线 JSON（X=轴向变形 mm，Y=轴向荷载 kN）"),
                new("ConfiningPressure", ColumnType.Real, Comment: "试验围压 σ3（kPa；本方法 30 / 60 / 100，每级 3 件；单件只做一级）"),
                new("InitialHeight", ColumnType.Real, Comment: "试件初始高度 h0（mm；3 次 120° 测量均值，合格窗 306±5）"),
                new("InitialDiameter", ColumnType.Real, Comment: "试件初始直径 d0（mm；标准件 Φ100，上 / 中 / 下各测 3 次取均值）"),
                new("Mass", ColumnType.Real, Comment: "试件总质量（g；试验后称量，规范 4.7）"),
                new("WaterContent", ColumnType.Real, Comment: "含水率 w（%；按本规程 T 1206 测定，精确至 0.1%）"),
                new("DryDensity", ColumnType.Real, Comment: "干密度（g/cm³；报告 6.7）"),
                new("WaterContentLoss", ColumnType.Real, Comment: "含水率损失（%；报告 6.7）"),
                new("MaxAxialLoad", ColumnType.Real, Comment: "最大轴向荷载 Pmax（N；规范 4.6 取值口径，式 T 1236-3）"),
                new("PeakDeformation", ColumnType.Real, Comment: "取 Pmax 时的轴向变形 Δh（mm）"),
                new("AxialStrain", ColumnType.Real, Comment: "破坏时轴向应变 ε1（%；式 T 1236-1）"),
                new("ShearStrength", ColumnType.Real, Comment: "试件剪切强度 σ1max（kPa；式 T 1236-3）"),
                new("StoppedAt15PercentStrain", ColumnType.Integer, Comment: "是否因轴向变形超过 15% 而取 15% 处荷载（0/1；规范 4.6）"),
                new("IsEffective", ColumnType.Integer, Comment: "是否有效（0/1；现役 Effective 语义；本规范未给剔除规则，先预留）"),
                new("SampleDesc", ColumnType.Text, Comment: "试样描述（文本，允许空）"),
                new("InvalidReason", ColumnType.Text, Comment: "无效原因（文本，允许空）"),
            ],
            Comment: "抗剪强度（静三轴）试件明细表"
        );

    /// <summary>
    /// 试验单模板表：只放"同产品重复度高"的默认值（照现役土工三轴模板的「仪器 + 人员」口径，
    /// 加上 6.4 / 6.6 的缩分与成型信息）。列名与主表同名，试验单弹窗按同名键预填。
    /// </summary>
    public TableDefinition TemplateTable { get; } =
        new(
            TemplateTableName,
            ["TemplateID"],
            [
                new("TemplateID", ColumnType.Text, IsRequired: true, Comment: "模板主键（「<方法代号>-TEMPLATE」）"),
                new("MachineNameAndID", ColumnType.Text, Comment: "主要仪器设备名称、型号及编号（模板默认值）"),
                new("TestPerson", ColumnType.Text, Comment: "试验员（模板默认值）"),
                new("CalculatePerson", ColumnType.Text, Comment: "计算人（模板默认值）"),
                new("VerificationPerson", ColumnType.Text, Comment: "校核人（模板默认值）"),
                new("SampleReductionMethod", ColumnType.Text, Comment: "缩分方法（模板默认值）"),
                new("MoldingMethod", ColumnType.Text, Comment: "成型方法（模板默认值）"),
                new("MoldingParameters", ColumnType.Text, Comment: "成型主要参数（模板默认值）"),
            ],
            Comment: "抗剪强度（静三轴）试验单模板表"
        );
}
