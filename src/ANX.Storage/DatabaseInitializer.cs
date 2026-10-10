namespace ANX.Storage;

using ANX.Core.Methods;
using Microsoft.Data.Sqlite;

/// <summary>SQLite 库的落地位置。</summary>
public sealed record StorageOptions(string DatabasePath);

/// <summary>
/// 建库 / 校验架构版本。**不做迁移**：版本对不上直接报错，提示备份后重建（既定决策）。
/// </summary>
public sealed class DatabaseInitializer(StorageOptions options, IEnumerable<IMethodPersistenceModule> methods)
{
    private readonly IReadOnlyList<IMethodPersistenceModule> _methods = [.. methods];

    /// <summary>建库（幂等）并校验版本；返回实际使用的架构版本。</summary>
    public int Initialize()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var sql in SchemaBuilder.BuildFrameworkTables())
        {
            Execute(connection, transaction, sql);
        }

        foreach (var method in _methods)
        {
            Execute(connection, transaction, SchemaBuilder.BuildTable(method.MainTable));
            Execute(connection, transaction, SchemaBuilder.BuildTable(method.DetailTable));
            Execute(connection, transaction, SchemaBuilder.BuildTable(method.TemplateTable));
        }

        var version = ReadVersion(connection, transaction, out var hasVersion);
        if (!hasVersion)
        {
            Execute(
                connection,
                transaction,
                $"INSERT INTO {SchemaBuilder.DbVersionTable} (Version) VALUES ({SchemaBuilder.CurrentSchemaVersion});"
            );
            version = SchemaBuilder.CurrentSchemaVersion;
        }
        else if (version != SchemaBuilder.CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"数据库架构版本不匹配（库为 {version}，程序要求 {SchemaBuilder.CurrentSchemaVersion}）。"
                    + "本项目不做迁移：请备份旧库后重建。"
            );
        }

        transaction.Commit();
        return version;
    }

    /// <summary>打开一个连接（调用方负责释放）。</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={options.DatabasePath}");
        connection.Open();
        return connection;
    }

    private static int ReadVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        out bool hasVersion
    )
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT Version FROM {SchemaBuilder.DbVersionTable} LIMIT 1;";

        var value = command.ExecuteScalar();
        hasVersion = value is not null and not DBNull;
        return hasVersion ? Convert.ToInt32(value) : 0;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
