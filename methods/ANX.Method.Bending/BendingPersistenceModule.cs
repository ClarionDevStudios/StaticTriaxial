namespace ANX.Method.Bending;

using ANX.Core.Methods;

/// <summary>
/// T 0715 的三张表。字段取自规范条款（对现役 `SQL\T0715-2025_BendingTest.sql` 头部的
/// 「规范条款 ↔ 字段」清单逐条复核），数值列一律用真实类型。
/// </summary>
public sealed class BendingPersistenceModule : IMethodPersistenceModule
{
    public const string MainTableName = "BendingTestMain";

    public const string DetailTableName = "BendingTestDetail";

    public const string TemplateTableName = "BendingTestMainTemplate";

    public string MethodCode => "T0715";

    public TableDefinition MainTable { get; } = new(
        MainTableName,
        "SheetID",
        [
            // 头信息（规范 1.2 / 3.4 / 7.x）
            new ColumnDefinition("SheetID", ColumnType.Text, IsRequired: true),
            new ColumnDefinition("ProjectName", ColumnType.Text),
            new ColumnDefinition("TestObjective", ColumnType.Text), // 常温 15℃ / 低温 −10℃
            new ColumnDefinition("MixtureType", ColumnType.Text),
            new ColumnDefinition("TestTemperature", ColumnType.Real), // ℃
            new ColumnDefinition("LoadRate", ColumnType.Real), // mm/min
            new ColumnDefinition("SpanLength", ColumnType.Real), // 跨径 L，mm
            new ColumnDefinition("Operator", ColumnType.Text),
            new ColumnDefinition("CreatedOn", ColumnType.Text), // yyyy-MM-dd
            new ColumnDefinition("CompletedOn", ColumnType.Text),
            new ColumnDefinition("EffectiveCount", ColumnType.Integer),
            new ColumnDefinition("ExcludedCount", ColumnType.Integer),
            new ColumnDefinition("NeedsSupplement", ColumnType.Integer), // 0/1
            // 组统计列（规范 5.5）：强度 0.01MPa、应变 1με
            new ColumnDefinition("FlexuralStrengthAvg", ColumnType.Real),
            new ColumnDefinition("FlexuralStrengthStdDev", ColumnType.Real),
            new ColumnDefinition("FlexuralStrengthCvPercent", ColumnType.Real),
            new ColumnDefinition("MaxBendingStrainAvg", ColumnType.Real),
            new ColumnDefinition("MaxBendingStrainStdDev", ColumnType.Real),
            new ColumnDefinition("MaxBendingStrainCvPercent", ColumnType.Real),
        ]
    );

    public TableDefinition DetailTable { get; } = new(
        DetailTableName,
        "SampleKey",
        [
            new ColumnDefinition("SampleKey", ColumnType.Text, IsRequired: true), // SheetID + 试件号
            new ColumnDefinition("SheetID", ColumnType.Text, IsRequired: true),
            new ColumnDefinition("SampleID", ColumnType.Text),
            new ColumnDefinition("SampleLength", ColumnType.Real), // mm
            new ColumnDefinition("SampleWidth", ColumnType.Real), // b
            new ColumnDefinition("SampleHeight", ColumnType.Real), // h
            // 单件判定值（规范 5.1~5.4）
            new ColumnDefinition("PeakLoadValue", ColumnType.Real), // P_B，kN
            new ColumnDefinition("PeakDeflection", ColumnType.Real), // d，mm
            new ColumnDefinition("OriginCorrection", ColumnType.Real), // O′，mm
            new ColumnDefinition("CorrectedDeflection", ColumnType.Real), // 修正后挠度
            new ColumnDefinition("TangentK", ColumnType.Real),
            new ColumnDefinition("TangentB", ColumnType.Real),
            new ColumnDefinition("FlexuralStrength", ColumnType.Real), // MPa
            new ColumnDefinition("MaxBendingTensileStrain", ColumnType.Real), // με
            new ColumnDefinition("IsEffective", ColumnType.Integer), // 0/1
            new ColumnDefinition("InvalidReason", ColumnType.Text),
            new ColumnDefinition("ChartData", ColumnType.Text), // 曲线序列化（紧凑 JSON）
            new ColumnDefinition("CreateDate", ColumnType.Text),
            new ColumnDefinition("Remark", ColumnType.Text),
        ]
    );

    public TableDefinition TemplateTable { get; } = new(
        TemplateTableName,
        "TemplateID",
        [
            new ColumnDefinition("TemplateID", ColumnType.Text, IsRequired: true),
            new ColumnDefinition("ProjectName", ColumnType.Text),
            new ColumnDefinition("TestObjective", ColumnType.Text),
            new ColumnDefinition("MixtureType", ColumnType.Text),
            new ColumnDefinition("TestTemperature", ColumnType.Real),
            new ColumnDefinition("LoadRate", ColumnType.Real),
            new ColumnDefinition("SpanLength", ColumnType.Real),
            new ColumnDefinition("Operator", ColumnType.Text),
            new ColumnDefinition("CreateDate", ColumnType.Text),
        ]
    );
}
