namespace ANX.Method.Marshall.Tests;

using ANX.Core.Methods;

/// <summary>
/// 三表结构"照现役"的守卫（用户 2026-10-16 定的长期口径：**后续方法的字段名必须与现役一致**，
/// 例外只有表名与纯新增列）。
///
/// 期望列名/列序**逐列抄自现役真库**
/// （`E:\Work\沥青力学试验软件\沥青综合性能试验软件\Bins\Data\AsphaltMechanicalTesting.db`
/// 的 `AsphaltMarshallStabilityMain` 37 列 / `Detail` 32 列 / `MainTemplate` 14 列），
/// 所以这份用例同时是"将来别手滑改名"的闸门。
/// </summary>
public sealed class MarshallSchemaTests
{
    private readonly MarshallPersistenceModule _module = new();

    /// <summary>现役 AsphaltMarshallStabilityMain 的 37 列（原样照抄）。</summary>
    private static readonly string[] LegacyMainColumns =
    [
        "UUID",
        "SheetID",
        "ClientUnits",
        "SupervisionUnits",
        "ProjectName",
        "TestSpecificationsID",
        "TestSpecificationsDesc",
        "TestStandardsID",
        "TestStandardsDesc",
        "SampleSizeID",
        "SampleSizeDesc",
        "AsphaltMixtureType",
        "SampleDensity",
        "VoidRatio",
        "AsphaltUsage",
        "ContentByVolume",
        "MineralAggregateVoidRatio",
        "AsphaltSaturation",
        "Group1AvgFlowValue",
        "Group2AvgFlowValue",
        "Group1AvgStability",
        "Group2AvgStability",
        "Group1Stability_Std_Dev",
        "Group2Stability_Std_Dev",
        "Group1Stability_Cv_Percent",
        "Group2Stability_Cv_Percent",
        "Group1Flow_Std_Dev",
        "Group2Flow_Std_Dev",
        "Group1Flow_Cv_Percent",
        "Group2Flow_Cv_Percent",
        "StabilityRatio",
        "TestTemperatureDesc",
        "Tester",
        "AcceptanceDate",
        "CreatedOn", // 现役 CreateDate（C# 侧沿用弯曲命名：框架 TestSheetService 认 CreatedOn）
        "AvailabilityCalculated",
        "SheetIndex",
    ];

    /// <summary>现役 AsphaltMarshallStabilityDetail 的 32 列（原样照抄）。</summary>
    private static readonly string[] LegacyDetailColumns =
    [
        "UUID",
        "SheetID",
        "SampleID",
        "SampleDesc",
        "GroupID",
        "GroupDesc",
        "TestTemperatureDesc",
        "SampleInitialHeight",
        "SampleInitialDiameter",
        "StrainRate",
        "CorrectionFactor",
        "LoadPeakValue",
        "DeformationPeakValue",
        "MSRaw",
        "MSCorrected",
        "FlowValue",
        "MarshallModulus",
        "OPrime",
        "KFactor",
        "PeakX",
        "PeakY",
        "TangentK",
        "TangentB",
        "IsObvious",
        "ChartData",
        "EndPointIndex",
        "ReportData",
        "Effective",
        "TestStatus",
        "CreateDate",
        "SheetIndex",
        "IsSuspiciousNoPeak",
    ];

    /// <summary>现役 AsphaltMarshallStabilityMainTemplate 的 14 列；主键 SheetID → C# TemplateID。</summary>
    private static readonly string[] LegacyTemplateColumns =
    [
        "SheetID", // → C# TemplateID（与弯曲同款：模板主键不叫 SheetID）
        "ClientUnits",
        "SupervisionUnits",
        "ProjectName",
        "TestSpecificationsID",
        "TestSpecificationsDesc",
        "TestStandardsID",
        "TestStandardsDesc",
        "SampleSizeID",
        "SampleSizeDesc",
        "TestTemperatureDesc",
        "Tester",
        "CreateDate",
        "SheetIndex",
    ];

    [Fact]
    public void 主表的列要覆盖现役全部列()
    {
        AssertColumnsCovered(_module.MainTable, LegacyMainColumns, allowedMissing: []);
    }

    [Fact]
    public void 明细表的列要覆盖现役全部列()
    {
        AssertColumnsCovered(_module.DetailTable, LegacyDetailColumns, allowedMissing: []);
    }

    [Fact]
    public void 模板表的列要覆盖现役全部列()
    {
        // 唯一的改名：现役模板主键叫 SheetID，C# 侧叫 TemplateID（与弯曲同款）
        AssertColumnsCovered(_module.TemplateTable, LegacyTemplateColumns, allowedMissing: ["SheetID"]);
    }

    [Fact]
    public void 每一列都要有中文含义注释()
    {
        foreach (var table in new[] { _module.MainTable, _module.DetailTable, _module.TemplateTable })
        {
            foreach (var column in table.Columns)
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(column.Comment),
                    $"{table.TableName}.{column.Name} 缺中文含义注释"
                );
            }
        }
    }

    [Fact]
    public void 表名与代号按既定口径()
    {
        Assert.Equal("T0709", _module.MethodCode);
        Assert.Equal("MarshallTestMain", MarshallPersistenceModule.MainTableName);
        Assert.Equal("MarshallTestDetail", MarshallPersistenceModule.DetailTableName);
        Assert.Equal("MarshallTestTemplate", MarshallPersistenceModule.TemplateTableName);
    }

    /// <summary>现役每一列都要在 C# 表里有落点（<paramref name="allowedMissing"/> = 允许改名的少数几列）。</summary>
    private static void AssertColumnsCovered(
        TableDefinition table,
        string[] legacyColumns,
        string[] allowedMissing
    )
    {
        var actual = table.Columns.Select(column => column.Name).ToHashSet(StringComparer.Ordinal);

        var missing = legacyColumns
            .Where(name => !allowedMissing.Contains(name, StringComparer.Ordinal))
            .Where(name => !actual.Contains(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"{table.TableName} 缺现役列：{string.Join("、", missing)}"
        );
    }
}
