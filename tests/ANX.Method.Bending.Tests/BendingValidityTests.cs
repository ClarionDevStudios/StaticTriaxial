namespace ANX.Method.Bending.Tests;

using System.Globalization;
using ANX.Control;
using ANX.Core;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 有效性计算（现役弯曲窗体的「有效性计算」按钮 → CalcAndSaveBendingStats）：
/// 按 T 0715-2025 5.6 剔除与平均值偏差超过允许误差（400με）的试件，把有效性写回明细、
/// 组统计写回主表；**不**改试验单的完成状态。
/// </summary>
public sealed class BendingValidityTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-valid-{Guid.NewGuid():N}"
    );

    public BendingValidityTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 有效性计算应剔除超差试件并写回主表与明细()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();
        var sheets = new TestSheetService(
            initializer,
            new SheetNumberGenerator(initializer),
            [module]
        );
        var execution = new BendingExecution(
            sheets,
            new BendingParameters(new ParameterStore(initializer))
        );

        var sheetId = await execution.CreateSheetAsync("默认项目", "WT");

        // 4 件：3 件的最大弯拉应变挨得很近，第 4 件（14000με）偏离平均值 3000με > 允许误差 400με
        var strains = new[] { 10000.0, 10100.0, 9900.0, 14000.0 };
        var strengths = new[] { 8.0, 8.1, 7.9, 11.0 };

        for (var i = 0; i < strains.Length; i++)
        {
            await sheets.AppendSampleAsync(
                sheetId,
                (i + 1).ToString(CultureInfo.InvariantCulture),
                [
                    new CurvePoint(0.0, 0.0),
                    new CurvePoint(1.0, strengths[i] / 2),
                    new CurvePoint(2.0, strengths[i]),
                ],
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleWidth"] = 30,
                    ["SampleHeight"] = 35,
                    ["PeakLoadValue"] = strengths[i],
                    ["FlexuralStrength"] = strengths[i],
                    ["MaxBendingTensileStrain"] = strains[i],
                    ["IsEffective"] = 1,
                }
            );
        }

        var report = await execution.EvaluateValidityAsync(sheetId);

        Assert.Equal(4, report.Evaluated);
        Assert.Equal(3, report.EffectiveCount);
        Assert.Equal(["4"], report.ExcludedSampleIds);
        Assert.False(report.NeedsSupplement);

        // 明细：第 4 件标记为无效，其余有效
        var rows = sheets.ReadDetailRows(sheetId, ["SampleID", "IsEffective"]);
        var effectiveBySample = rows.ToDictionary(
            row => Convert.ToString(row["SampleID"], CultureInfo.InvariantCulture)!,
            row => Convert.ToDouble(row["IsEffective"], CultureInfo.InvariantCulture),
            StringComparer.Ordinal
        );
        Assert.Equal(1.0, effectiveBySample["1"]);
        Assert.Equal(1.0, effectiveBySample["2"]);
        Assert.Equal(1.0, effectiveBySample["3"]);
        Assert.Equal(0.0, effectiveBySample["4"]);

        // 主表：有效 3 / 剔除 1，均值只按有效件算（(8.0+8.1+7.9)/3 = 8.0）
        var main = sheets.ReadMainRow(
            sheetId,
            ["EffectiveCount", "ExcludedCount", "NeedsSupplement", "FlexuralStrengthAvg"]
        );
        Assert.Equal(3.0, Convert.ToDouble(main["EffectiveCount"], CultureInfo.InvariantCulture));
        Assert.Equal(1.0, Convert.ToDouble(main["ExcludedCount"], CultureInfo.InvariantCulture));
        Assert.Equal(0.0, Convert.ToDouble(main["NeedsSupplement"], CultureInfo.InvariantCulture));
        Assert.Equal(8.0, Convert.ToDouble(main["FlexuralStrengthAvg"], CultureInfo.InvariantCulture), 6);
    }

    [Fact]
    public async Task 没有判定结果的试验单做有效性计算应返回零件()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "empty.db")),
            [module]
        );
        initializer.Initialize();
        var sheets = new TestSheetService(
            initializer,
            new SheetNumberGenerator(initializer),
            [module]
        );
        var execution = new BendingExecution(
            sheets,
            new BendingParameters(new ParameterStore(initializer))
        );

        var sheetId = await execution.CreateSheetAsync("默认项目", "WT");

        var report = await execution.EvaluateValidityAsync(sheetId);

        Assert.Equal(0, report.Evaluated);
        Assert.True(report.NeedsSupplement);
        Assert.Empty(report.ExcludedSampleIds);
    }
}
