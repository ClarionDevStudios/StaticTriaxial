namespace ANX.Method.Marshall.Tests;

using ANX.Core;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 马歇尔三表不光"列齐"，还要**真能建单、真能存一件、真能读回来** ——
/// 用现役的列名写值（`MSRaw` / `MSCorrected` / `FlowValue` / `MarshallModulus` / `GroupID` …），
/// 这就是"字段名与现役一致"最直接的一次实跑。
/// </summary>
public sealed class MarshallPersistenceRoundTripTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"anx-marshall-{Guid.NewGuid():N}");

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
    public async Task 建单与存一件试样能走通_值按现役列名落库()
    {
        var module = new MarshallPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(DatabasePath),
            [module]
        );
        initializer.Initialize();

        var sheets = new TestSheetService(
            initializer,
            new SheetNumberGenerator(initializer),
            [module]
        );

        var sheetId = await sheets.CreateAsync("T0709", "马歇尔自检", "MS");
        Assert.StartsWith("MS", sheetId, StringComparison.Ordinal);

        List<CurvePoint> curve = [new(0, 0), new(1.2, 4.6), new(2.5, 9.8), new(3.0, 5.1)];

        await sheets.AppendSampleAsync(
            sheetId,
            "S001",
            curve,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["GroupID"] = 1,
                ["SampleInitialDiameter"] = 101.6,
                ["SampleInitialHeight"] = 63.5,
                ["StrainRate"] = 50,
                ["CorrectionFactor"] = 1.0,
                ["LoadPeakValue"] = 9.8,
                ["DeformationPeakValue"] = 2.5,
                ["MSRaw"] = 9.8,
                ["MSCorrected"] = 9.8,
                ["FlowValue"] = 25,
                ["MarshallModulus"] = 3.92,
                ["Effective"] = 1,
                ["TestStatus"] = 2,
            }
        );

        var rows = sheets.ReadDetailRows(
            sheetId,
            [
                "SampleID",
                "GroupID",
                "LoadPeakValue",
                "MSRaw",
                "MSCorrected",
                "FlowValue",
                "MarshallModulus",
                "SampleInitialDiameter",
                "SampleInitialHeight",
                "StrainRate",
            ]
        );

        var row = Assert.Single(rows);
        Assert.Equal("S001", Convert.ToString(row["SampleID"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(1.0, Number(row["GroupID"]), 6);
        Assert.Equal(9.8, Number(row["MSRaw"]), 6);
        Assert.Equal(9.8, Number(row["MSCorrected"]), 6);
        Assert.Equal(25.0, Number(row["FlowValue"]), 6);
        Assert.Equal(3.92, Number(row["MarshallModulus"]), 6);
        Assert.Equal(101.6, Number(row["SampleInitialDiameter"]), 6);
        Assert.Equal(63.5, Number(row["SampleInitialHeight"]), 6);
        Assert.Equal(50.0, Number(row["StrainRate"]), 6);

        // 曲线也按 C# 既有口径进了 ChartData
        var stored = sheets.ReadCurve(sheetId, "S001");
        Assert.Equal(curve.Count, stored.Count);
        Assert.Equal(9.8, stored[^2].Y, 3);
    }

    private static double Number(object? value) =>
        Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
}
