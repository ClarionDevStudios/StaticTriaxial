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

    /// <summary>框架自有表：方法参数（方法代号 + 参数键 → 参数值）。</summary>
    public const string ParameterTable = "TestParameterConfig";

    /// <summary>
    /// 建库时写入的架构版本；对不上就报错提示"备份后重建"，不做迁移。
    /// 2 → 3：T0715 三表按现役结构补齐（工程信息 / 体积指标 / 统计口径等），并给每列加含义注释。
    /// 3 → 4：明细表去掉自造的 `SampleKey`，主键改为现役口径的 `(SheetID, SampleID)`；
    ///        试件号改为现役格式 `S001`（序号落 `SheetIndex`）。
    /// </summary>
    public const int CurrentSchemaVersion = 4;

    /// <summary>框架自有表的建表 SQL（幂等）。</summary>
    public static IEnumerable<string> BuildFrameworkTables()
    {
        yield return $"""
            -- 表 {DbVersionTable}：架构版本（本项目不做迁移，版本对不上直接报错提示备份后重建）
            CREATE TABLE IF NOT EXISTS {DbVersionTable} (
                Version INTEGER NOT NULL,  -- 架构版本号；当前程序要求 {CurrentSchemaVersion}
                PRIMARY KEY (Version)
            );
            """;

        yield return $"""
            -- 表 {SheetSequenceTable}：单号流水（前缀 + 日期 → 当天已用到的流水号）
            CREATE TABLE IF NOT EXISTS {SheetSequenceTable} (
                Prefix       TEXT    NOT NULL,  -- 单号前缀，如 WT
                SheetDate    TEXT    NOT NULL,  -- 单号日期（yyyyMMdd）
                LastSequence INTEGER NOT NULL,  -- 该前缀该日期已用到的最末流水号（3 位，从 001 起）
                PRIMARY KEY (Prefix, SheetDate)
            );
            """;

        // 参数集中配置：方法代号 + 参数键 → 参数值（键值对，不给每个参数建强类型列）
        yield return $"""
            -- 表 {ParameterTable}：方法参数集中配置（键值对，不给每个参数建强类型列）
            CREATE TABLE IF NOT EXISTS {ParameterTable} (
                MethodCode TEXT NOT NULL,  -- 方法代号，如 T0715
                ParamKey   TEXT NOT NULL,  -- 参数键，如 LoadRate
                ParamValue TEXT,           -- 参数值（一律按文本存，读时按声明类型解析）
                PRIMARY KEY (MethodCode, ParamKey)
            );
            """;
    }

    /// <summary>
    /// 按声明生成一张方法表的建表 SQL（幂等）。每列后面跟 <c>-- 含义</c> 注释，
    /// 列名与类型对齐排版，方便直接读 SQL 审核字段。
    /// </summary>
    public static string BuildTable(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var declarations = table
            .Columns.Select(column => (
                Column: column,
                Text: $"{column.Name} {ToSqlType(column.Type)}"
                    + (column.IsRequired ? " NOT NULL" : string.Empty)
            ))
            .ToArray();

        var width = declarations.Max(item => item.Text.Length);

        var builder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(table.Comment))
        {
            builder.Append("-- 表 ").Append(table.TableName).Append("：").AppendLine(table.Comment);
        }

        builder.Append("CREATE TABLE IF NOT EXISTS ").Append(table.TableName).AppendLine(" (");

        foreach (var (column, text) in declarations)
        {
            builder.Append("    ").Append(text).Append(',');

            if (!string.IsNullOrWhiteSpace(column.Comment))
            {
                // 逗号紧跟声明，注释再按最宽列对齐 —— 这样 SQLite 解析没问题，人读也整齐
                builder.Append(' ', width - text.Length + 2);
                builder.Append("-- ").Append(column.Comment);
            }

            builder.AppendLine();
        }

        builder
            .Append("    PRIMARY KEY (")
            .Append(string.Join(", ", table.PrimaryKeyColumns))
            .AppendLine(")");
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
