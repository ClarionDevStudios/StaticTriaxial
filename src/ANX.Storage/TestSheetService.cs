namespace ANX.Storage;

using ANX.Core;
using ANX.Core.Methods;
using ANX.Core.TestSheets;
using Microsoft.Data.Sqlite;

/// <summary>
/// 试验单生命周期与查询。框架只知道"表与列的名字"，不知道任何字段的含义 ——
/// 字段从方法声明的 <see cref="IMethodPersistenceModule"/> 里取。
/// </summary>
public sealed class TestSheetService : ITestSheetService
{
    private readonly DatabaseInitializer _initializer;
    private readonly SheetNumberGenerator _numbers;
    private readonly Dictionary<string, IMethodPersistenceModule> _methods;

    public TestSheetService(
        DatabaseInitializer initializer,
        SheetNumberGenerator numbers,
        IEnumerable<IMethodPersistenceModule> methods
    )
    {
        _initializer = initializer;
        _numbers = numbers;
        _methods = methods.ToDictionary(m => m.MethodCode, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task<string> CreateAsync(
        string methodCode,
        string projectName,
        string numberPrefix,
        CancellationToken cancellationToken = default
    )
    {
        var method = ResolveMethod(methodCode);
        var number = _numbers.Next(numberPrefix, DateOnly.FromDateTime(DateTime.Now));

        using var connection = _initializer.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var sheetId = number.ToString();
        Insert(
            connection,
            transaction,
            method.MainTable,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["SheetID"] = sheetId,
                ["ProjectName"] = projectName,
                ["CreatedOn"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            }
        );

        transaction.Commit();
        return Task.FromResult(sheetId);
    }

    /// <inheritdoc />
    public Task AppendSampleAsync(
        string sheetId,
        string sampleId,
        IReadOnlyList<CurvePoint> curve,
        IReadOnlyDictionary<string, double> results,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(results);

        var method = ResolveMethodForSheet(sheetId);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SampleKey"] = $"{sheetId}-{sampleId}",
            ["SheetID"] = sheetId,
            ["SampleID"] = sampleId,
            ["ChartData"] = CurveJson.Encode(curve),
            ["CreateDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        foreach (var (name, value) in results)
        {
            if (!IsDeclared(method.DetailTable, name))
            {
                // 不静默忽略：方法写了个不存在的列，多半是拼错了字段名。
                throw new ArgumentException($"方法 {method.MethodCode} 的明细表没有声明列 '{name}'", nameof(results));
            }

            values[name] = value;
        }

        using var connection = _initializer.OpenConnection();
        using var transaction = connection.BeginTransaction();
        Insert(connection, transaction, method.DetailTable, values);
        transaction.Commit();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 覆盖某个试件明细行上的**结果列**（按 SampleKey 定位；曲线与创建时间不动）。
    /// 供"按当前参数重算历史曲线"用。
    /// </summary>
    public Task UpdateDetailFieldsAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, double> results,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(results);

        var method = ResolveMethodForSheet(sheetId);
        var assignments = new List<string>();

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();

        var index = 0;
        foreach (var (name, value) in results)
        {
            if (!IsDeclared(method.DetailTable, name))
            {
                throw new ArgumentException(
                    $"方法 {method.MethodCode} 的明细表没有声明列 '{name}'",
                    nameof(results)
                );
            }

            var parameter = $"$v{index++}";
            assignments.Add($"{name} = {parameter}");
            command.Parameters.AddWithValue(parameter, value);
        }

        if (assignments.Count == 0)
        {
            return Task.CompletedTask;
        }

        command.CommandText =
            $"UPDATE {method.DetailTable.TableName} SET {string.Join(", ", assignments)} WHERE SampleKey = $key;";
        command.Parameters.AddWithValue("$key", $"{sheetId}-{sampleId}");
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CompleteAsync(string sheetId, CancellationToken cancellationToken = default)
    {
        var method = ResolveMethodForSheet(sheetId);

        using var connection = _initializer.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"UPDATE {method.MainTable.TableName} SET CompletedOn = $completed WHERE SheetID = $sheetId;";
            command.Parameters.AddWithValue("$completed", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("$sheetId", sheetId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 把一组统计写进主表。字段名由调用方（方法层）给出，框架只做"声明过的列才写"的校验。
    /// </summary>
    public Task UpdateMainFieldsAsync(
        string sheetId,
        IReadOnlyDictionary<string, double> fields,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(fields);
        var method = ResolveMethodForSheet(sheetId);

        using var connection = _initializer.OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var (name, value) in fields)
        {
            if (!IsDeclared(method.MainTable, name))
            {
                throw new ArgumentException($"方法 {method.MethodCode} 的主表没有声明列 '{name}'", nameof(fields));
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"UPDATE {method.MainTable.TableName} SET {name} = $value WHERE SheetID = $sheetId;";
            command.Parameters.AddWithValue("$value", value);
            command.Parameters.AddWithValue("$sheetId", sheetId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string methodCode,
        string? keyword = null,
        CancellationToken cancellationToken = default
    )
    {
        var method = ResolveMethod(methodCode);

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();

        // 先过滤、最后投影（EF Core 的口径同理；这里用 SQL 直写）
        command.CommandText = $"""
            SELECT m.SheetID,
                   m.ProjectName,
                   m.CreatedOn,
                   (SELECT COUNT(1) FROM {method.DetailTable.TableName} d WHERE d.SheetID = m.SheetID)
            FROM {method.MainTable.TableName} m
            WHERE ($keyword IS NULL OR m.SheetID LIKE $like OR m.ProjectName LIKE $like)
            ORDER BY m.SheetID;
            """;
        command.Parameters.AddWithValue("$keyword", (object?)keyword ?? DBNull.Value);
        command.Parameters.AddWithValue("$like", $"%{keyword}%");

        var result = new List<TestSheetSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(
                new TestSheetSummary(
                    reader.GetString(0),
                    methodCode,
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    DateOnly.Parse(reader.GetString(2)[..10]),
                    reader.GetInt32(3)
                )
            );
        }

        return Task.FromResult<IReadOnlyList<TestSheetSummary>>(result);
    }

    /// <summary>读回一条明细的曲线（回放用）。</summary>
    public IReadOnlyList<CurvePoint> ReadCurve(string sampleKey)
    {
        var sheetId = sampleKey[..Math.Max(0, sampleKey.LastIndexOf('-'))];
        var method = ResolveMethodForSheet(sheetId);

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT ChartData FROM {method.DetailTable.TableName} WHERE SampleKey = $key;";
        command.Parameters.AddWithValue("$key", sampleKey);

        return CurveJson.Decode(command.ExecuteScalar() as string);
    }

    /// <summary>读主表指定的列（键为列名；列名必须在方法声明里存在）。</summary>
    public IReadOnlyDictionary<string, object?> ReadMainRow(
        string sheetId,
        IReadOnlyList<string> columns
    )
    {
        var method = ResolveMethodForSheet(sheetId);
        foreach (var column in columns)
        {
            if (!IsDeclared(method.MainTable, column))
            {
                throw new ArgumentException($"方法 {method.MethodCode} 的主表没有声明列 '{column}'", nameof(columns));
            }
        }

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", columns)} FROM {method.MainTable.TableName} WHERE SheetID = $sheetId;";
        command.Parameters.AddWithValue("$sheetId", sheetId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"找不到试验单 {sheetId}");
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            result[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return result;
    }

    /// <summary>读明细行（按 SampleID 排序），每行一个"列名 → 值"字典。</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadDetailRows(
        string sheetId,
        IReadOnlyList<string> columns
    )
    {
        var method = ResolveMethodForSheet(sheetId);
        foreach (var column in columns)
        {
            if (!IsDeclared(method.DetailTable, column))
            {
                throw new ArgumentException($"方法 {method.MethodCode} 的明细表没有声明列 '{column}'", nameof(columns));
            }
        }

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", columns)} FROM {method.DetailTable.TableName} WHERE SheetID = $sheetId ORDER BY SampleID;";
        command.Parameters.AddWithValue("$sheetId", sheetId);

        var rows = new List<IReadOnlyDictionary<string, object?>>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private IMethodPersistenceModule ResolveMethod(string methodCode) =>
        _methods.TryGetValue(methodCode, out var method)
            ? method
            : throw new InvalidOperationException($"未注册的方法代号：{methodCode}");

    /// <summary>按单号前缀反查方法：单号形如 `WT20261010001`，前缀由项目定义，这里按"能解析出日期"来识别。</summary>
    private IMethodPersistenceModule ResolveMethodForSheet(string sheetId)
    {
        foreach (var method in _methods.Values)
        {
            using var connection = _initializer.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(1) FROM {method.MainTable.TableName} WHERE SheetID = $sheetId;";
            command.Parameters.AddWithValue("$sheetId", sheetId);
            if (Convert.ToInt32(command.ExecuteScalar()) > 0)
            {
                return method;
            }
        }

        throw new InvalidOperationException($"找不到单号 {sheetId} 对应的试验单");
    }

    private static bool IsDeclared(TableDefinition table, string columnName) =>
        table.Columns.Any(c => string.Equals(c.Name, columnName, StringComparison.Ordinal));

    private static void Insert(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TableDefinition table,
        IReadOnlyDictionary<string, object?> values
    )
    {
        var columns = values.Keys.ToArray();
        var parameters = columns.Select((_, i) => $"$p{i}").ToArray();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"INSERT INTO {table.TableName} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)});";

        for (var i = 0; i < columns.Length; i++)
        {
            command.Parameters.AddWithValue(parameters[i], values[columns[i]] ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }
}
