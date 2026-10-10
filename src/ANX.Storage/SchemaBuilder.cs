namespace ANX.Storage;

using System.Text;
using ANX.Core.Methods;

/// <summary>
/// 按 <see cref="TableDefinition"/> 生成建表 SQL。
/// 之所以用"声明 → DDL"而不是给每个方法建 EF Core 实体：方法的表在方法工程里，
/// 而框架工程不能引用方法工程（铁律），声明式表定义正好把这条边界守住。
/// </summary>
public static class SchemaBuilder
{
    /// <summary>框架自有表：数据库版本。</summary>
    public const string DbVersionTable = "DbVersion";

    /// <summary>框架自有表：单号流水（前缀 + 日期 → 当天已用到的流水号）。</summary>
    public const string SheetSequenceTable = "SheetSequence";

    /// <summary>建库时写入的架构版本；对不上就报错提示"备份后重建"，不做迁移。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>框架自有表的建表 SQL（幂等）。</summary>
    public static IEnumerable<string> BuildFrameworkTables()
    {
        yield return $"""
            CREATE TABLE IF NOT EXISTS {DbVersionTable} (
                Version INTEGER NOT NULL
            );
            """;

        yield return $"""
            CREATE TABLE IF NOT EXISTS {SheetSequenceTable} (
                Prefix       TEXT    NOT NULL,
                SheetDate    TEXT    NOT NULL,
                LastSequence INTEGER NOT NULL,
                PRIMARY KEY (Prefix, SheetDate)
            );
            """;
    }

    /// <summary>按声明生成一张方法表的建表 SQL（幂等）。</summary>
    public static string BuildTable(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var builder = new StringBuilder();
        builder.Append("CREATE TABLE IF NOT EXISTS ").Append(table.TableName).AppendLine(" (");

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            builder
                .Append("    ")
                .Append(column.Name)
                .Append(' ')
                .Append(ToSqlType(column.Type));

            if (column.IsRequired)
            {
                builder.Append(" NOT NULL");
            }

            builder.AppendLine(i == table.Columns.Count - 1 ? string.Empty : ",");
        }

        builder.Append("    , PRIMARY KEY (").Append(table.PrimaryKeyColumn).AppendLine(")");
        builder.Append(");");

        return builder.ToString();
    }

    private static string ToSqlType(ColumnType type) =>
        type switch
        {
            ColumnType.Integer => "INTEGER",
            ColumnType.Real => "REAL",
            ColumnType.Text => "TEXT",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知列类型"),
        };
}
