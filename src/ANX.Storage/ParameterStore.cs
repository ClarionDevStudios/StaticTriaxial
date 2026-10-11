namespace ANX.Storage;

using Microsoft.Data.Sqlite;

/// <summary>
/// 方法参数的读写（框架级、与字段含义无关）：方法代号 + 参数键 → 参数值。
/// 参数集中存库、不散落到 INI（既定决策：只留引导层必需项在文件里）。
/// </summary>
public sealed class ParameterStore(DatabaseInitializer initializer)
{
    /// <summary>读某方法的全部参数。</summary>
    public IReadOnlyDictionary<string, string> Load(string methodCode)
    {
        using var connection = initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT ParamKey, ParamValue FROM {SchemaBuilder.ParameterTable} WHERE MethodCode = $method;";
        command.Parameters.AddWithValue("$method", methodCode);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return values;
    }

    /// <summary>写入（存在则覆盖）某个方法的若干参数。</summary>
    public void Save(string methodCode, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        using var connection = initializer.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var (key, value) in values)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO {SchemaBuilder.ParameterTable} (MethodCode, ParamKey, ParamValue)
                VALUES ($method, $key, $value)
                ON CONFLICT (MethodCode, ParamKey) DO UPDATE SET ParamValue = $value;
                """;
            command.Parameters.AddWithValue("$method", methodCode);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value ?? string.Empty);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
