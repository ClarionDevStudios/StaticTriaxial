namespace ANX.Method.Bending.Tests;

using ANX.Control;
using ANX.Core;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 参数（试样尺寸 / 机器参数）存库并按分组给两个页面用；
/// 关键点：**改了试件高度，判定就必须按新高度算、并落进明细行**。
/// </summary>
public sealed class BendingParametersTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-param-{Guid.NewGuid():N}"
    );

    public BendingParametersTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 参数应按分组声明且默认值可载入()
    {
        var parameters = CreateParameters();

        Assert.Equal(3, parameters.Definitions.Count(d => d.Group == ParameterGroup.Sample));
        Assert.Equal(4, parameters.Definitions.Count(d => d.Group == ParameterGroup.Machine));

        var values = await parameters.LoadAsync();
        Assert.Equal("200", values[BendingParameters.SpanLengthKey]);
        Assert.Equal("35", values[BendingParameters.SampleHeightKey]);
    }

    [Fact]
    public async Task 存过的参数应覆盖默认值()
    {
        var parameters = CreateParameters();

        await parameters.SaveAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingParameters.SampleHeightKey] = "40",
                [BendingParameters.LoadRateKey] = "55",
            }
        );

        var values = await parameters.LoadAsync();
        Assert.Equal("40", values[BendingParameters.SampleHeightKey]);
        Assert.Equal("55", values[BendingParameters.LoadRateKey]);
        Assert.Equal("30", values[BendingParameters.SampleWidthKey]); // 没存过的回落到默认
    }

    [Fact]
    public async Task 改试件高度后判定应按新高度计算并落库()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();
        var sheets = new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
        var parameters = new BendingParameters(new ParameterStore(initializer));
        var execution = new BendingExecution(sheets, parameters);

        var sheetId = await sheets.CreateAsync("T0715", "参数验证", "WT");
        var curve = Enumerable.Range(0, 40).Select(i => new CurvePoint(i * 0.1, i * 0.2)).ToArray();

        // 曲线峰值 7.8kN；默认 h=35：R_B = 3·200·7800/(2·30·1225) = 63.67
        var before = await execution.CompleteSampleAsync(sheetId, "1", curve);
        Assert.NotNull(before);
        Assert.Equal(63.67, before!["flexuralStrength"], 0.01);

        // 把高度改成 40 后再跑一件：强度应随高度平方下降
        await parameters.SaveAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [BendingParameters.SampleHeightKey] = "40",
            }
        );

        var after = await execution.CompleteSampleAsync(sheetId, "2", curve);
        Assert.NotNull(after);
        Assert.Equal(48.75, after!["flexuralStrength"], 0.01); // 同一荷载、h=40 → 3·200·7800/(2·30·1600)

        // 明细行里记的也是新高度
        var rows = sheets.ReadDetailRows(sheetId, ["SampleID", "SampleHeight"]);
        Assert.Equal(2, rows.Count);
        Assert.Equal(40, Convert.ToDouble(rows[1]["SampleHeight"]));
    }

    private BendingParameters CreateParameters()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();

        return new BendingParameters(new ParameterStore(initializer));
    }
}
