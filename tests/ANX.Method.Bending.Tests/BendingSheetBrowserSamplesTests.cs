namespace ANX.Method.Bending.Tests;

using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 历史数据页「试件」列表的数据来源（用户 2026-10-15："SampleID 及列名信息要和当前语言一致"）：
/// 浏览器**不再回库里的列名**，回的是**列名的显示资源键** —— 界面按当前语言取列名，
/// 又保留"界面不认识任何具体字段"这条设计（列名只在方法工程里出现）。
/// </summary>
public sealed class BendingSheetBrowserSamplesTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"anx-browser-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_root, "Data", "anx.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task 试件行的键是显示资源键_而不是库里的列名()
    {
        var (sheets, execution, parameters) = Create();
        var sheetId = await execution.CreateSheetAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ProjectName"] = "列名自检",
                ["TestSpecificationsId"] = "0",
                ["TestObjectiveId"] = "0",
                ["SampleSizeId"] = "0",
                ["MixtureType"] = "AC-13",
                ["TestTemperature"] = "15",
                ["LoadRate"] = "2",
            }
        );

        await sheets.AppendSampleAsync(
            sheetId,
            "S001",
            [new(0, 0), new(1, 3.5)],
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["SampleWidth"] = 30,
                ["SampleHeight"] = 35,
                ["SpanLength"] = 200,
                ["PeakLoadValue"] = 3.5,
                ["FlexuralStrength"] = 8.0,
                ["MaxBendingTensileStrain"] = 1000,
                ["IsEffective"] = 1,
            }
        );

        var browser = new BendingSheetBrowser(sheets, parameters);
        var rows = await browser.ReadSamplesAsync(sheetId);

        var row = Assert.Single(rows);

        // 第一列就是试件号本身（列表标题要用），值照旧
        Assert.Equal("S001", row[BendingMetricKeys.SampleId]);

        // 全部键都是**方法声明的**资源键（资源存在性由 CurveAxisResourceTests 对着语言字典查）
        IEnumerable<string> keys = row.Keys;
        Assert.All(keys, key => Assert.Contains(key, BendingMetricKeys.All));

        // 库里那几个列名一个都不该露出来
        Assert.DoesNotContain("SampleID", keys);
        Assert.DoesNotContain("PeakLoadValue", keys);
        Assert.DoesNotContain("MaxBendingTensileStrain", keys);
    }

    private (TestSheetService Sheets, BendingExecution Execution, BendingParameters Parameters) Create()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(new StorageOptions(DatabasePath), [module]);
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
