namespace ANX.Method.Bending.Tests;

using ANX.Core.Methods;

/// <summary>
/// 三表结构"按现役补齐"的守卫。现役 `SQL\T0715-2025_BendingTest.sql` 的每一列都要在新结构里有落点
/// （C# 侧改过名的在注释里标 `现役名 → C#名`），并且每列都必须带中文含义注释 —— 生成的建表 SQL 要能直接读。
/// 现役列数：主表 37 / 明细 27 / 模板 31。
/// </summary>
public sealed class BendingSchemaAlignmentTests
{
    private readonly BendingPersistenceModule _module = new();

    /// <summary>现役 AsphaltBendingTestMain 的 37 列 → C# BendingTestMain 的落点。</summary>
    private static readonly string[] LegacyMainColumns =
    [
        "UUID",
        "SheetID",
        "ClientUnits",
        "SupervisionUnits",
        "ProjectName",
        "TestSpecificationsId", // 现役 TestSpecificationsID
        "TestSpecificationsDesc",
        "TestObjectiveId", // 现役 TestObjectiveID
        "TestObjective", // 现役 TestObjectiveDesc
        "TestTemperature", // 现役 TestTemperatureDesc（文本 → REAL）
        "LoadRate",
        "SampleSizeId", // 现役 SampleSizeID
        "SampleSizeDesc",
        "MixtureType", // 现役 AsphaltMixtureType
        "FormingMethodId", // 现役 FormingMethodID
        "FormingMethod",
        "SampleDensity",
        "VoidRatio",
        "AsphaltUsage",
        "ContentByVolume",
        "MineralAggregateVoidRatio",
        "AsphaltSaturation",
        "StdDevMethodId", // 现役 StdDevMethodID
        "StdDevMethodDesc",
        "OutlierDetectionMethodId", // 现役 OutlierDetectionMethodID
        "OutlierDetectionMethodDesc",
        "AllowedError",
        "FlexuralStrengthAvg",
        "FlexuralStrengthStdDev", // 现役 FlexuralStrength_Std_Dev
        "FlexuralStrengthCvPercent", // 现役 FlexuralStrength_Cv_Percent
        "MaxBendingStrainAvg", // 现役 MaxBendingTensileStrainAvg
        "MaxBendingStrainStdDev", // 现役 MaxBendingTensileStrain_Std_Dev
        "MaxBendingStrainCvPercent", // 现役 MaxBendingTensileStrain_Cv_Percent
        "Operator", // 现役 Tester
        "AcceptanceDate",
        "CreatedOn", // 现役 CreateDate
        "SheetIndex",
    ];

    /// <summary>现役 AsphaltBendingTestDetail 的 27 列（去掉故意不搬的 HasStraightLine / IsObvious）→ C# 落点。</summary>
    private static readonly string[] LegacyDetailColumns =
    [
        "UUID",
        "SheetID",
        "SampleID",
        "SampleDesc",
        "TestTemperature", // 现役 TestTemperatureDesc
        "SampleLength",
        "SampleWidth",
        "SampleHeight",
        "SpanLength",
        "StrainRate",
        "PeakLoadValue", // 现役 LoadPeakValue
        "PeakDeflection", // 现役 DeformationPeakValue
        "CorrectedDeflection", // 现役 DeformationCorrected
        "OriginCorrection", // 现役 OPrime
        "TangentK",
        "TangentB",
        "FlexuralStrength",
        "MaxBendingTensileStrain",
        "ChartData",
        "EndPointIndex",
        "IsEffective", // 现役 Effective
        "Remark",
        "TestStatus",
        "CreateDate",
        "SheetIndex",
    ];

    /// <summary>现役 AsphaltBendingTestMainTemplate 的 31 列 → C# BendingTestMainTemplate 的落点。</summary>
    private static readonly string[] LegacyTemplateColumns =
    [
        "UUID",
        "TemplateID", // 现役主键 SheetID
        "SheetIndex",
        "ProjectName",
        "ClientUnits",
        "SupervisionUnits",
        "TestSpecificationsId",
        "TestSpecificationsDesc",
        "TestObjectiveId",
        "TestObjective",
        "TestTemperature",
        "LoadRate",
        "SampleSizeId",
        "SampleSizeDesc",
        "MixtureType",
        "FormingMethodId",
        "FormingMethod",
        "SampleDensity",
        "VoidRatio",
        "AsphaltUsage",
        "ContentByVolume",
        "MineralAggregateVoidRatio",
        "AsphaltSaturation",
        "StdDevMethodId",
        "StdDevMethodDesc",
        "OutlierDetectionMethodId",
        "OutlierDetectionMethodDesc",
        "AllowedError",
        "Operator",
        "AcceptanceDate",
        "CreateDate",
    ];

    public static TheoryData<string[], TableDefinition> Tables()
    {
        var module = new BendingPersistenceModule();

        return new TheoryData<string[], TableDefinition>
        {
            { LegacyMainColumns, module.MainTable },
            { LegacyDetailColumns, module.DetailTable },
            { LegacyTemplateColumns, module.TemplateTable },
        };
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void 每张表都应覆盖现役对应的每一列(string[] legacyColumns, TableDefinition table)
    {
        var declared = table.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var missing = legacyColumns.Where(name => !declared.Contains(name)).ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void 现役列数应与对照清单一致()
    {
        // 37 / 27-2 / 31 —— 数字变了就说明现役脚本换了版本，对照清单要跟着复核
        Assert.Equal(37, LegacyMainColumns.Length);
        Assert.Equal(25, LegacyDetailColumns.Length);
        Assert.Equal(31, LegacyTemplateColumns.Length);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void 每列都必须带含义注释(string[] _, TableDefinition table)
    {
        Assert.False(string.IsNullOrWhiteSpace(table.Comment), $"{table.TableName} 缺少表说明");

        var uncommented = table
            .Columns.Where(c => string.IsNullOrWhiteSpace(c.Comment))
            .Select(c => c.Name)
            .ToArray();

        Assert.Empty(uncommented);
    }

    [Fact]
    public void 明细表不应搬入已抛弃的修正直线列()
    {
        // 服务于"修正直线平移 1.5mm 求交点"那条已抛弃的路径
        var declared = _module.DetailTable.Columns.Select(c => c.Name).ToArray();

        Assert.DoesNotContain("HasStraightLine", declared);
        Assert.DoesNotContain("IsObvious", declared);
    }

    [Fact]
    public void 模板表不应包含组统计列与运行结果列()
    {
        var declared = _module.TemplateTable.Columns.Select(c => c.Name).ToArray();

        Assert.DoesNotContain("FlexuralStrengthAvg", declared);
        Assert.DoesNotContain("FlexuralStrengthStdDev", declared);
        Assert.DoesNotContain("FlexuralStrengthCvPercent", declared);
        Assert.DoesNotContain("MaxBendingStrainAvg", declared);
        Assert.DoesNotContain("MaxBendingStrainStdDev", declared);
        Assert.DoesNotContain("MaxBendingStrainCvPercent", declared);
        Assert.DoesNotContain("CompletedOn", declared);
        Assert.DoesNotContain("EffectiveCount", declared);
        Assert.DoesNotContain("ExcludedCount", declared);
        Assert.DoesNotContain("NeedsSupplement", declared);
    }

    [Fact]
    public void 主表应保留框架与报表依赖的既有列名()
    {
        // 改名会静默打断 BendingReportBuilder / TestSheetService 的取数
        var declared = _module.MainTable.Columns.Select(c => c.Name).ToArray();

        Assert.Contains("ProjectName", declared);
        Assert.Contains("TestObjective", declared);
        Assert.Contains("TestTemperature", declared);
        Assert.Contains("LoadRate", declared);
        Assert.Contains("SpanLength", declared);
        Assert.Contains("EffectiveCount", declared);
        Assert.Contains("FlexuralStrengthAvg", declared);
        Assert.Contains("MaxBendingStrainAvg", declared);
        Assert.Contains("CreatedOn", declared);
    }
}
