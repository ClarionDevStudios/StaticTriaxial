namespace ANX.Method.Bending.Tests;

using ANX.Control;
using ANX.Core;
using ANX.Core.Reporting;
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

    /// <summary>
    /// 曲线图上要**带标注**（用户 2026-10-15：实时曲线画了修正直线 / 修正原点 / 拟合区间两端，
    /// 存盘图里没有）。
    ///
    /// 两件用**同一条曲线**、同一个图题（<see cref="FixedTexts"/> 不给占位符），
    /// 唯一的差别只有落库的拟合结果列 —— 于是"两张图不一样"只能是标注画进去了。
    /// </summary>
    [Fact]
    public async Task 落库的拟合结果会画进曲线图()
    {
        var (sheets, execution, _) = Create();

        var sheetId = await execution.CreateSheetAsync(FullFields());
        List<CurvePoint> curve =
        [
            new(0, 0),
            new(0.2, 0.6),
            new(0.4, 1.4),
            new(0.6, 2.0),
            new(0.8, 1.5),
        ];

        await sheets.AppendSampleAsync(sheetId, "S001", curve, Results(withFit: true));
        await sheets.AppendSampleAsync(sheetId, "S002", curve, Results(withFit: false));

        var service = new BendingReportExportService(sheets, ReportRoot, new FixedTexts());
        var withFit = await service.SaveCurveImageAsync(sheetId, "S001", "zh-CN");
        var withoutFit = await service.SaveCurveImageAsync(sheetId, "S002", "zh-CN");

        Assert.NotNull(withFit);
        Assert.NotNull(withoutFit);
        Assert.False(
            File.ReadAllBytes(withFit!).AsSpan().SequenceEqual(File.ReadAllBytes(withoutFit!)),
            "S001 落了拟合结果，图上应当多出修正直线 / 修正原点 / 拟合区间两端"
        );
    }

    /// <summary>图题与轴名固定、不带占位符（两件图照着同一段文字画，好比对）。</summary>
    private sealed class FixedTexts : ITextResolver
    {
        public string Get(string key, string culture) => key;
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

    /// <summary>
    /// 明细行的判定值与尺寸（曲线图这一步不看它们，只是让这一件像"跑过"）。
    /// <paramref name="withFit"/> = true 时连拟合结果列一起落库（图上会因此多出标注）。
    /// </summary>
    private static Dictionary<string, double> Results(bool withFit = false)
    {
        var results = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["SampleWidth"] = 30,
            ["SampleHeight"] = 35,
            ["SpanLength"] = 200,
            ["PeakLoadValue"] = 3.5,
            ["FlexuralStrength"] = 8.0,
            ["MaxBendingTensileStrain"] = 1000,
            ["IsEffective"] = 1,
        };

        if (withFit)
        {
            results["TangentK"] = 4.0;
            results["TangentB"] = -0.1;
            results["OriginCorrection"] = 0.025;
        }

        return results;
    }
}
