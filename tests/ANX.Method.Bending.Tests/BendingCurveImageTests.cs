namespace ANX.Method.Bending.Tests;

using ANX.Control;
using ANX.Core;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 曲线图自动存盘（用户 2026-10-13 提、2026-10-14 确认"按当初口径补上"）：
/// 跑完一件、落库之后，这一件的曲线图要出现在 `Report\弯曲\<单号>\<试样号>.png`。
/// 这里测方法层那个"只存图"的入口 —— 与导出报表共用同一份渲染实现。
/// </summary>
public sealed class BendingCurveImageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"anx-curve-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_root, "Data", "anx.db");

    private string ReportRoot => Path.Combine(_root, "Report");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task 落库之后存曲线图_文件出现在报表目录里()
    {
        var (sheets, execution, _) = Create();

        var sheetId = await execution.CreateSheetAsync(FullFields());
        const string sampleId = "S001";

        // 一件真实曲线（两点就够，测的是"有没有写文件、写到哪"）+ 判定值一起落库
        List<CurvePoint> curve = [new(0, 0), new(1, 3.5)];
        await sheets.AppendSampleAsync(sheetId, sampleId, curve, Results());

        var service = new BendingReportExportService(sheets, ReportRoot);
        var path = await service.SaveCurveImageAsync(sheetId, sampleId, "zh-CN");

        Assert.NotNull(path);
        Assert.True(File.Exists(path), $"应当写出曲线图：{path}");
        Assert.EndsWith(Path.Combine("弯曲", sheetId, $"{sampleId}.png"), path, StringComparison.Ordinal);
        Assert.True(new FileInfo(path!).Length > 0, "图片不能是空文件");
    }

    [Fact]
    public async Task 没有曲线的件不写空图()
    {
        var (sheets, execution, _) = Create();

        var sheetId = await execution.CreateSheetAsync(FullFields());
        const string sampleId = "S001";

        // 只填了信息、没跑过的件：曲线为空
        await sheets.AppendSampleAsync(sheetId, sampleId, [], Results());

        var service = new BendingReportExportService(sheets, ReportRoot);
        var path = await service.SaveCurveImageAsync(sheetId, sampleId, "zh-CN");

        Assert.Null(path);
        Assert.False(Directory.Exists(Path.Combine(ReportRoot, "弯曲", sheetId)));
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

    private static Dictionary<string, string> FullFields() =>
        new(StringComparer.Ordinal)
        {
            ["ProjectName"] = "曲线图自检",
            ["TestSpecificationsId"] = "0",
            ["TestObjectiveId"] = "0",
            ["SampleSizeId"] = "0",
            ["MixtureType"] = "AC-13",
            ["TestTemperature"] = "15",
            ["LoadRate"] = "2",
        };

    /// <summary>明细行的判定值与尺寸（曲线图这一步不看它们，只是让这一件像"跑过"）。</summary>
    private static Dictionary<string, double> Results() =>
        new(StringComparer.Ordinal)
        {
            ["SampleWidth"] = 30,
            ["SampleHeight"] = 35,
            ["SpanLength"] = 200,
            ["PeakLoadValue"] = 3.5,
            ["FlexuralStrength"] = 8.0,
            ["MaxBendingTensileStrain"] = 1000,
            ["IsEffective"] = 1,
        };
}
