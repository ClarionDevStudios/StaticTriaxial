namespace ANX.Method.Bending.Tests;

using ANX.Control;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 试验单编辑（用户 2026-10-14）：字段声明照现役次序、新建按字段落库（含 `XxxId + XxxDesc` 成对列）、
/// 单上的加载速率写回参数表、模板往返。
/// </summary>
public sealed class BendingSheetEditorTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"anx-sheet-editor-{Guid.NewGuid():N}.db"
    );

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public void 字段声明照现役版面两列按行配对()
    {
        var (_, execution, _) = Create();
        var fields = execution.SheetInfoFields;
        var keys = fields.Select(f => f.Key).ToArray();

        // 现役窗体是两列网格：左列工程与试验条件、右列体积指标 6 项 + 接样日期 / 试验时间 / 试验员，
        // 两列按行配对。界面用两列 UniformGrid 按行填充，所以声明顺序就是"一左一右"（用户 2026-10-14）。
        Assert.Equal(
            [
                "SheetID", "SampleDensity",              // r1
                "ClientUnits", "VoidRatio",              // r2
                "SupervisionUnits", "AsphaltUsage",       // r3
                "ProjectName", "ContentByVolume",         // r4
                "TestSpecificationsId", "AsphaltSaturation", // r5
                "MixtureType", "MineralAggregateVoidRatio",  // r6
                "SampleSizeId", "AcceptanceDate",          // r7
                "TestObjectiveId", "CreatedOn",            // r8
                "TestTemperature", "Operator",             // r9
                "LoadRate",                                // r10
            ],
            keys
        );

        // 体积指标 6 项单独成组（用户 2026-10-14：允许留空 → 界面据此提示）
        var volume = fields.Where(f => f.Group == ParameterGroup.SheetVolume).ToArray();
        Assert.Equal(
            ["SampleDensity", "VoidRatio", "AsphaltUsage", "ContentByVolume", "AsphaltSaturation", "MineralAggregateVoidRatio"],
            volume.Select(f => f.Key)
        );

        // 必填项带 " *"（照现役 ValidatingInput 的清单；体积指标不带）
        var required = fields.Where(f => f.DisplayName.EndsWith(" *", StringComparison.Ordinal)).Select(f => f.Key);
        Assert.Equal(
            [
                "ProjectName",
                "TestSpecificationsId",
                "MixtureType",
                "SampleSizeId",
                "TestObjectiveId",
                "TestTemperature",
                "LoadRate",
            ],
            required
        );

        // 三个下拉的选项数照现役：规范 1 条、试验目的 2 条、试件尺寸 3 档
        Assert.Single(fields.First(f => f.Key == "TestSpecificationsId").Options!);
        Assert.Equal(2, fields.First(f => f.Key == "TestObjectiveId").Options!.Count);
        Assert.Equal(3, fields.First(f => f.Key == "SampleSizeId").Options!.Count);
    }

    [Fact]
    public async Task 新建试验单按字段落库_加载速率写回参数表()
    {
        var (sheets, execution, parameters) = Create();

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ProjectName"] = "G15 高速 K12+300",
            ["ClientUnits"] = "某委托单位",
            ["SupervisionUnits"] = "某监理公司",
            ["TestSpecificationsId"] = "0",
            ["TestObjectiveId"] = "1",
            ["SampleSizeId"] = "2",
            ["MixtureType"] = "AC-13",
            ["TestTemperature"] = "-10",
            ["LoadRate"] = "5",
            ["Operator"] = "张三",
            ["AcceptanceDate"] = "2026-10-14",
            ["SampleDensity"] = "2.45",
            ["VoidRatio"] = string.Empty, // 体积指标允许留空
        };

        var sheetId = await execution.CreateSheetAsync(fields);
        Assert.StartsWith("WT", sheetId, StringComparison.Ordinal);

        // 读回：选填项留空也读得出来（回空串）
        var loaded = await execution.ReadSheetInfoAsync(sheetId);
        Assert.Equal("G15 高速 K12+300", loaded["ProjectName"]);
        Assert.Equal("2", loaded["SampleSizeId"]);
        Assert.Equal("2.45", loaded["SampleDensity"]);
        Assert.Equal(string.Empty, loaded["VoidRatio"]);

        // 现役口径的成对描述列也写了（Id + Desc）
        var row = sheets.ReadMainRow(
            sheetId,
            ["TestObjective", "SampleSizeDesc", "TestSpecificationsDesc"]
        );
        Assert.Equal("低温弯拉性能", Convert.ToString(row["TestObjective"]));
        Assert.Equal("(250×50x50)mm", Convert.ToString(row["SampleSizeDesc"]));
        Assert.Equal("T 0715-2025 沥青混合料弯曲试验", Convert.ToString(row["TestSpecificationsDesc"]));

        // 单上的加载速率写回参数表（实际控制按它走）
        var stored = await parameters.LoadAsync();
        Assert.Equal("5", stored["LoadRate"]);
    }

    [Fact]
    public async Task 必填项缺失时新建报错_体积指标可以留空()
    {
        var (_, execution, _) = Create();

        // 一项都不填 → 项目名称先报
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => execution.CreateSheetAsync(new Dictionary<string, string>(StringComparer.Ordinal))
        );

        // 逐个把必填项抠掉，都要报（照现役 ValidatingInput 的清单）
        foreach (var key in RequiredKeys())
        {
            var fields = FullFields();
            fields.Remove(key);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => execution.CreateSheetAsync(fields)
            );
        }

        // 温度 / 速率要能解析成数字（现役 TryStrToFloat / TryStrToInt）
        var bad = FullFields();
        bad["TestTemperature"] = "常温";
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution.CreateSheetAsync(bad));

        // 体积指标留空**不算错**（用户 2026-10-14）
        var ok = FullFields();
        ok["SampleDensity"] = string.Empty;
        ok["VoidRatio"] = string.Empty;
        var sheetId = await execution.CreateSheetAsync(ok);
        Assert.StartsWith("WT", sheetId, StringComparison.Ordinal);
    }

    private static string[] RequiredKeys() =>
    [
        "ProjectName",
        "TestSpecificationsId",
        "TestObjectiveId",
        "SampleSizeId",
        "MixtureType",
        "TestTemperature",
        "LoadRate",
    ];

    /// <summary>一份"必填项齐全、体积指标留空"的表单。</summary>
    private static Dictionary<string, string> FullFields() =>
        RequiredKeys()
            .ToDictionary(
                key => key,
                key =>
                    key switch
                    {
                        "ProjectName" => "G15 高速 K12+300",
                        "MixtureType" => "AC-13",
                        "TestTemperature" => "15",
                        "LoadRate" => "50",
                        _ => "0",
                    },
                StringComparer.Ordinal
            );

    [Fact]
    public async Task 模板可以存也可以读()
    {
        var (_, execution, _) = Create();

        Assert.False(await execution.HasTemplateAsync());

        await execution.SaveTemplateAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ProjectName"] = "模板项目",
                ["ClientUnits"] = "模板委托单位",
                ["TestObjectiveId"] = "1",
                ["LoadRate"] = "20",
                ["SampleDensity"] = "2.5",
            }
        );

        Assert.True(await execution.HasTemplateAsync());

        var template = await execution.ReadTemplateAsync();
        Assert.Equal("模板项目", template["ProjectName"]);
        Assert.Equal("模板委托单位", template["ClientUnits"]);
        Assert.Equal("20", template["LoadRate"]);
        Assert.Equal("2.5", template["SampleDensity"]);

        // 模板里没存的字段回空（而不是抛）
        Assert.Equal(string.Empty, template["MixtureType"]);
    }

    [Fact]
    public void 试验目的变化时方法给出联动的温度与速率()
    {
        var (_, execution, _) = Create();

        var warm = execution.ResolveDependentDefaults("TestObjectiveId", "0");
        Assert.Equal("15", warm["TestTemperature"]);
        Assert.Equal("50", warm["LoadRate"]);

        var cold = execution.ResolveDependentDefaults("TestObjectiveId", "1");
        Assert.Equal("-10", cold["TestTemperature"]);
        Assert.Equal("50", cold["LoadRate"]);

        // 其它字段没有联动
        Assert.Empty(execution.ResolveDependentDefaults("ProjectName", "随便"));
    }

    private (TestSheetService Sheets, BendingExecution Execution, BendingParameters Parameters) Create()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(new StorageOptions(_databasePath), [module]);
        initializer.Initialize();

        var sheets = new TestSheetService(
            initializer,
            new SheetNumberGenerator(initializer),
            [module]
        );
        var parameters = new BendingParameters(new ParameterStore(initializer));

        return (sheets, new BendingExecution(sheets, parameters), parameters);
    }
}
