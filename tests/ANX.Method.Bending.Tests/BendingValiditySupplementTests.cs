namespace ANX.Method.Bending.Tests;

using ANX.Core;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 规范 5.6：剔除后有效件不足 3 件 → **不写组结果**（均值列清空）、只写件数与"需补做"标志、
/// 逐个试件的有效性照写（用户 2026-10-13 定，与现役"本次不写组结果"同口径）。
/// </summary>
public sealed class BendingValiditySupplementTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"anx-supplement-{Guid.NewGuid():N}.db"
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
    public async Task 剔除后不足三件则不写组结果并置需补做()
    {
        var (sheets, execution) = Create();
        var sheetId = await sheets.CreateAsync("T0715", "需补做", "WT");

        // 三件：两件正常（应变 5000/5100）、一件超差（9000，偏差远超 400με）
        await AppendSampleAsync(sheets, execution, sheetId, "S001", strain: 5000, strength: 8.0);
        await AppendSampleAsync(sheets, execution, sheetId, "S002", strain: 5100, strength: 8.0);
        await AppendSampleAsync(sheets, execution, sheetId, "S003", strain: 9000, strength: 9.0);

        var report = await execution.EvaluateValidityAsync(sheetId);

        Assert.Equal(3, report.Evaluated);
        Assert.Equal(2, report.EffectiveCount);
        Assert.True(report.NeedsSupplement);
        Assert.Equal(["S003"], report.ExcludedSampleIds);

        // 组结果不写：均值列**清空**（不是留着旧值，也不是 0）
        var main = sheets.ReadMainRow(sheetId, ["FlexuralStrengthAvg", "MaxBendingStrainAvg"]);
        Assert.Null(main["FlexuralStrengthAvg"]);
        Assert.Null(main["MaxBendingStrainAvg"]);

        // 件数与"需补做"标志照写
        var flags = sheets.ReadMainRow(sheetId, ["EffectiveCount", "ExcludedCount", "NeedsSupplement"]);
        Assert.Equal(2, Convert.ToInt32(flags["EffectiveCount"]));
        Assert.Equal(1, Convert.ToInt32(flags["ExcludedCount"]));
        Assert.Equal(1, Convert.ToInt32(flags["NeedsSupplement"]));

        // 逐件有效性照写：被剔除的那件 IsEffective = 0
        Assert.Equal(
            0,
            Convert.ToInt32(sheets.ReadDetailRow(sheetId, "S003", ["IsEffective"])!["IsEffective"])
        );
    }

    /// <summary>
    /// 造一件"已完成"的试样：先把试样信息与曲线写好，再用判定器结算（与真跑一轮等价）。
    /// </summary>
    private static async Task AppendSampleAsync(
        TestSheetService sheets,
        BendingExecution execution,
        string sheetId,
        string sampleId,
        double strain,
        double strength
    )
    {
        // 曲线：上升段 + 峰值，峰值荷载按目标强度反推不了（这里只要求"能判定"），
        // 判定值直接由**结算**产生；目标应变通过试件高度间接控制不现实，
        // 所以这里结算后直接把判定列改成我们要的值（等价于"这台机器做出来就是这个数"）。
        var curve = Enumerable
            .Range(0, 40)
            .Select(i => new CurvePoint(i * 0.1, i * 0.2))
            .ToArray();
        await execution.CompleteSampleAsync(sheetId, sampleId, curve);

        await sheets.UpdateDetailFieldsAsync(
            sheetId,
            sampleId,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MaxBendingTensileStrain"] = strain,
                ["FlexuralStrength"] = strength,
            }
        );
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
