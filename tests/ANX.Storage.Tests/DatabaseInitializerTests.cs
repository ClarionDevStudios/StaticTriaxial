namespace ANX.Storage.Tests;

using ANX.Core.Methods;
using ANX.Method.Bending;
using Microsoft.Data.Sqlite;

/// <summary>建库、架构版本、单号流水、以及"数值列必须是真实类型"。</summary>
public sealed class DatabaseInitializerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"anx-test-{Guid.NewGuid():N}.db"
    );

    private readonly BendingPersistenceModule _module = new();

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    [Fact]
    public void 建库应创建方法三表与框架两表()
    {
        CreateInitializer().Initialize();

        var tables = QueryTableNames();

        Assert.Contains(BendingPersistenceModule.MainTableName, tables);
        Assert.Contains(BendingPersistenceModule.DetailTableName, tables);
        Assert.Contains(BendingPersistenceModule.TemplateTableName, tables);
        Assert.Contains(SchemaBuilder.DbVersionTable, tables);
        Assert.Contains(SchemaBuilder.SheetSequenceTable, tables);
    }

    [Fact]
    public void 架构版本应写入且重复初始化不报错()
    {
        var initializer = CreateInitializer();

        Assert.Equal(SchemaBuilder.CurrentSchemaVersion, initializer.Initialize());
        Assert.Equal(SchemaBuilder.CurrentSchemaVersion, initializer.Initialize()); // 幂等
    }

    [Fact]
    public void 架构版本不匹配应提示备份后重建()
    {
        CreateInitializer().Initialize();

        using (var connection = OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"UPDATE {SchemaBuilder.DbVersionTable} SET Version = 99;";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<SchemaVersionMismatchException>(
            () => CreateInitializer().Initialize()
        );

        // 版本号与库路径都随异常带出来（宿主据此弹提示框），不只是给一句文字
        Assert.Equal(99, exception.DatabaseVersion);
        Assert.Equal(SchemaBuilder.CurrentSchemaVersion, exception.RequiredVersion);
        Assert.Equal(_databasePath, exception.DatabasePath);
        Assert.Contains("备份旧库后重建", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 数值列必须是真实类型而不是文本()
    {
        CreateInitializer().Initialize();

        var types = QueryColumnTypes(BendingPersistenceModule.DetailTableName);

        Assert.Equal("REAL", types["SampleWidth"]);
        Assert.Equal("REAL", types["FlexuralStrength"]);
        Assert.Equal("TEXT", types["SampleID"]);
        Assert.Equal("TEXT", types["ChartData"]);
    }

    [Fact]
    public void 单号流水应从001起并按天递增()
    {
        var initializer = CreateInitializer();
        initializer.Initialize();
        var generator = new SheetNumberGenerator(initializer);

        var today = new DateOnly(2026, 10, 10);
        var tomorrow = today.AddDays(1);

        Assert.Equal("WT20261010001", generator.Next("WT", today).ToString());
        Assert.Equal("WT20261010002", generator.Next("WT", today).ToString());
        Assert.Equal("WT20261010003", generator.Next("WT", today).ToString());

        // 换一天重新从 001 起；不同前缀各算各的
        Assert.Equal("WT20261011001", generator.Next("WT", tomorrow).ToString());
        Assert.Equal("XY20261010001", generator.Next("XY", today).ToString());

        // 与纯函数的口径一致：解析回来还是同一个单号
        Assert.True(ANX.Core.TestSheets.SheetNumber.TryParse("WT20261010003", out var parsed));
        Assert.Equal(3, parsed.Sequence);
    }

    private DatabaseInitializer CreateInitializer() =>
        new(new StorageOptions(_databasePath), [new BendingPersistenceModule()]);

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private List<string> QueryTableNames()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private Dictionary<string, string> QueryColumnTypes(string table)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";

        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            types[reader.GetString(1)] = reader.GetString(2);
        }

        return types;
    }
}
