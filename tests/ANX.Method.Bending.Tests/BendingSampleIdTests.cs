namespace ANX.Method.Bending.Tests;

using ANX.Core;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 试件号自动生成（用户 2026-10-13）：照现役口径 —— `S` + 三位序号，
/// 序号 = 该单已有的 `MAX(SheetIndex) + 1`（现役 `GetMaxSampleIndex` + `FormatFloat('000', …)`）。
/// </summary>
public sealed class BendingSampleIdTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"anx-sampleid-{Guid.NewGuid():N}.db"
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
    public async Task 第一件是S001_之后按最大序号递增()
    {
        var (sheets, execution) = Create();
        var sheetId = await sheets.CreateAsync("T0715", "试件号验证", "WT");

        Assert.Equal("S001", await execution.NextSampleIdAsync(sheetId));

        // 结算要走判定器（拟合修正直线），曲线得像样：40 点纯上升段
        var curve = Enumerable
            .Range(0, 40)
            .Select(i => new CurvePoint(i * 0.1, i * 0.2))
            .ToArray();
        await execution.CompleteSampleAsync(sheetId, "S001", curve);

        Assert.Equal("S002", await execution.NextSampleIdAsync(sheetId));

        await execution.CompleteSampleAsync(sheetId, "S002", curve);
        Assert.Equal("S003", await execution.NextSampleIdAsync(sheetId));

        // 序号同时落进 SheetIndex（现役的 SheetIndex 与 SXXX 里的序号是同一个数）
        Assert.Equal(
            2,
            Convert.ToInt32(sheets.ReadDetailRow(sheetId, "S002", ["SheetIndex"])!["SheetIndex"])
        );
    }

    [Fact]
    public async Task 老记录是纯数字试件号时也能接着往后排()
    {
        var (sheets, execution) = Create();
        var sheetId = await sheets.CreateAsync("T0715", "老记录", "WT");

        // 老库的口径：试件号 1/2/3，SheetIndex 是空的
        var curve = new CurvePoint[] { new(0, 0), new(1, 5), new(2, 3) };
        await sheets.AppendSampleAsync(
            sheetId,
            "3",
            curve,
            new Dictionary<string, double>(StringComparer.Ordinal)
        );

        Assert.Equal("S004", await execution.NextSampleIdAsync(sheetId));
    }

    /// <summary>
    /// 跑完一件要留下"已完成"状态（现役 `tsCompleted = 2`）——
    /// 用户 2026-10-13 的置灰规则要按它判断"这件做完没做完"。
    /// </summary>
    [Fact]
    public async Task 跑完一件后明细的试验状态是完成()
    {
        var (sheets, execution) = Create();
        var sheetId = await sheets.CreateAsync("T0715", "状态验证", "WT");

        var curve = Enumerable
            .Range(0, 40)
            .Select(i => new CurvePoint(i * 0.1, i * 0.2))
            .ToArray();

        // 只填了信息、还没跑：不算完成
        await sheets.UpsertSampleInfoAsync(
            sheetId,
            "S001",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["SampleHeight"] = 35 }
        );
        Assert.False(Assert.Single(await execution.ReadSampleRowsAsync(sheetId)).IsCompleted);

        await execution.CompleteSampleAsync(sheetId, "S001", curve);

        var row = sheets.ReadDetailRow(sheetId, "S001", ["TestStatus"])!;
        Assert.Equal(2, Convert.ToInt32(row["TestStatus"])); // 现役 tsCompleted = 2

        // 列表接口也报"已完成"（界面据此置灰新建/编辑试样）
        Assert.True(Assert.Single(await execution.ReadSampleRowsAsync(sheetId)).IsCompleted);
    }

    /// <summary>
    /// 「有效性计算」按"有没有判定值"判（不是只看状态列）：只填了试样信息的件没有判定值，
    /// 跑完判定的才有 —— 用户 2026-10-13 要求"至少三件有判定值"才让点。
    /// </summary>
    [Fact]
    public async Task 只填信息的件没有判定值_跑完的才有()
    {
        var (sheets, execution) = Create();
        var sheetId = await sheets.CreateAsync("T0715", "判定值", "WT");

        await sheets.UpsertSampleInfoAsync(
            sheetId,
            "S001",
            new Dictionary<string, double>(StringComparer.Ordinal) { ["SampleHeight"] = 35 }
        );

        var onlyInfo = Assert.Single(await execution.ReadSampleRowsAsync(sheetId));
        Assert.False(onlyInfo.HasResult);
        Assert.False(onlyInfo.IsCompleted);

        var curve = Enumerable
            .Range(0, 40)
            .Select(i => new CurvePoint(i * 0.1, i * 0.2))
            .ToArray();
        await execution.CompleteSampleAsync(sheetId, "S001", curve);

        var done = Assert.Single(await execution.ReadSampleRowsAsync(sheetId));
        Assert.True(done.HasResult);
        Assert.True(done.IsCompleted);
    }

    private (TestSheetService Sheets, BendingExecution Execution) Create()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(new StorageOptions(_databasePath), [module]);
        initializer.Initialize();

        var sheets = new TestSheetService(
            initializer,
            new SheetNumberGenerator(initializer),
            [module]
        );

        return (sheets, new BendingExecution(sheets, new BendingParameters(new ParameterStore(initializer))));
    }
}
