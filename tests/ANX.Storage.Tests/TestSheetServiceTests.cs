namespace ANX.Storage.Tests;

using ANX.Core;
using ANX.Method.Bending;
using Microsoft.Data.Sqlite;

/// <summary>试验单完整生命周期：建单 → 加试件（含曲线）→ 写组统计 → 完成 → 查回。</summary>
public sealed class TestSheetServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"anx-sheet-{Guid.NewGuid():N}.db"
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
    public async Task 完整生命周期应能建单加试件写组统计并查回曲线()
    {
        var service = CreateService();

        var sheetId = await service.CreateAsync("T0715", "G15 高速 K12+300", "WT");
        Assert.StartsWith("WT", sheetId, StringComparison.Ordinal);

        var curve = Enumerable
            .Range(0, 20)
            .Select(i => new CurvePoint(i * 0.1, i * 0.5))
            .ToArray();

        await service.AppendSampleAsync(sheetId, "1", curve, Sample(strength: 73.47, strain: 23625));
        await service.AppendSampleAsync(sheetId, "2", curve, Sample(strength: 74.10, strain: 23700));

        await service.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["FlexuralStrengthAvg"] = 73.79,
                ["MaxBendingStrainAvg"] = 23663,
                ["EffectiveCount"] = 2,
            }
        );
        await service.CompleteAsync(sheetId);

        var sheets = await service.QueryAsync("T0715");
        Assert.Single(sheets);
        Assert.Equal(sheetId, sheets[0].SheetId);
        Assert.Equal("G15 高速 K12+300", sheets[0].ProjectName);
        Assert.Equal(2, sheets[0].SampleCount);

        // 曲线原样回放
        var restored = service.ReadCurve($"{sheetId}-1");
        Assert.Equal(curve.Length, restored.Count);
        Assert.Equal(curve[5], restored[5]);
    }

    [Fact]
    public async Task 写未声明的列应报错而不是静默忽略()
    {
        var service = CreateService();
        var sheetId = await service.CreateAsync("T0715", "项目", "WT");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AppendSampleAsync(
                sheetId,
                "1",
                [],
                new Dictionary<string, double>(StringComparer.Ordinal) { ["FlexualStrength"] = 1 }
            )
        );

        Assert.Contains("FlexualStrength", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 曲线JSON应能往返且足够紧凑()
    {
        var curve = Enumerable
            .Range(0, 30000)
            .Select(i => new CurvePoint(i * 0.02, Math.Sin(i * 0.01) * 5))
            .ToArray();

        var json = CurveJson.Encode(curve);

        Assert.StartsWith("[[0,0],[", json, StringComparison.Ordinal);
        var restored = CurveJson.Decode(json);
        Assert.Equal(curve.Length, restored.Count);

        // 序列化按 3 位小数（位移 0.001mm），因此是近似无损：往返误差不超过半个最小刻度
        Assert.Equal(curve[1234].X, restored[1234].X, 5e-4);
        Assert.Equal(curve[1234].Y, restored[1234].Y, 5e-4);
        Assert.True(json.Length < 700_000, $"3 万点应保持紧凑，实际 {json.Length} 字符");

        Assert.Empty(CurveJson.Decode(null));
        Assert.Empty(CurveJson.Decode("[]"));
    }

    private static Dictionary<string, double> Sample(double strength, double strain) =>
        new(StringComparer.Ordinal)
        {
            ["SampleWidth"] = 30,
            ["SampleHeight"] = 35,
            ["PeakLoadValue"] = 9.0,
            ["CorrectedDeflection"] = 4.5,
            ["FlexuralStrength"] = strength,
            ["MaxBendingTensileStrain"] = strain,
            ["IsEffective"] = 1,
        };

    private TestSheetService CreateService()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(new StorageOptions(_databasePath), [module]);
        initializer.Initialize();

        return new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
    }
}
