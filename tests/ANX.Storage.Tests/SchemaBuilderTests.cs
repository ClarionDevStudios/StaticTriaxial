namespace ANX.Storage.Tests;

using ANX.Core.Methods;
using ANX.Method.Bending;

/// <summary>建表 SQL 的形态：列名与类型对齐排版，每列后面都跟中文含义注释。</summary>
public sealed class SchemaBuilderTests
{
    private readonly BendingPersistenceModule _module = new();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void 生成的建表SQL应给每一列带注释(int tableIndex)
    {
        var table = tableIndex switch
        {
            0 => _module.MainTable,
            1 => _module.DetailTable,
            _ => _module.TemplateTable,
        };

        var sql = SchemaBuilder.BuildTable(table);
        var lines = sql.Split('\n').Select(l => l.TrimEnd()).ToArray();

        // 第一行是表说明，最后两行是主键与右括号
        Assert.StartsWith("-- 表 ", lines[0]);
        Assert.Contains($"CREATE TABLE IF NOT EXISTS {table.TableName} (", lines[1]);

        var columnLines = lines
            .Skip(2)
            .TakeWhile(l => !l.TrimStart().StartsWith("PRIMARY KEY", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(table.Columns.Count, columnLines.Length);

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var expected = $"-- {table.Columns[i].Comment}";
            Assert.EndsWith(expected, columnLines[i]);
            Assert.Contains(table.Columns[i].Name, columnLines[i]);
        }

        Assert.Contains($"PRIMARY KEY ({string.Join(", ", table.PrimaryKeyColumns)})", sql);
    }

    [Fact]
    public void 框架表建表SQL也带注释()
    {
        foreach (var sql in SchemaBuilder.BuildFrameworkTables())
        {
            Assert.Contains("--", sql);
        }
    }
}
