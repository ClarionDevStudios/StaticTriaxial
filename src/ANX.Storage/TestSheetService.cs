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
    /// <summary>记录唯一标识列名（主表与明细表同名）。云端按它去重，不能为空（用户 2026-10-13）。</summary>
    private const string UuidColumn = "UUID";

    /// <summary>试件排序索引列名。试件号里的序号就是它（现役 SheetIndex）。</summary>
    private const string SheetIndexColumn = "SheetIndex";

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
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["SheetID"] = sheetId,
            ["ProjectName"] = projectName,
            ["CreatedOn"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        // 主表带 UUID（用户 2026-10-13）：上传云端时按它去重，防同号单重复入库。
        // 现役同口径（`uEditBendingTestForm.pas`：`FRecAsphaltBendingTestMain.UUID := GenerateCleanGUID`）。
        if (IsDeclared(method.MainTable, UuidColumn))
        {
            values[UuidColumn] = RecordUuid.New();
        }

        Insert(connection, transaction, method.MainTable, values);

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
            ["SheetID"] = sheetId,
            ["SampleID"] = sampleId,
            ["ChartData"] = CurveJson.Encode(curve),
            ["CreateDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        };

        ApplySheetIndex(method.DetailTable, sampleId, values);

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

        // 明细沿用**同一张试验单的 UUID**（用户 2026-10-13；现役 `uBendingTestForm.pas:836`：
        // `FRecAsphaltBendingTestDetailInfo.UUID := FRecAsphaltBendingTestMain.UUID`）——
        // 上传云端时按 UUID 归组，单与它的试件是一份数据，不会各传各的。
        if (IsDeclared(method.DetailTable, UuidColumn))
        {
            values[UuidColumn] = ResolveSheetUuid(
                connection,
                transaction,
                method.MainTable,
                sheetId
            );
        }

        Insert(connection, transaction, method.DetailTable, values);
        transaction.Commit();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 覆盖某个试件明细行上的**结果列**（按 (SheetID, SampleID) 定位；曲线与创建时间不动）。
    /// 供"按当前参数重算历史曲线"用。
    /// </summary>
    public Task UpdateDetailFieldsAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, double> results,
        IReadOnlyList<CurvePoint>? curve = null,
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

        if (curve is not null)
        {
            assignments.Add("ChartData = $curve");
            command.Parameters.AddWithValue("$curve", CurveJson.Encode(curve));
        }

        if (assignments.Count == 0)
        {
            return Task.CompletedTask;
        }

        command.CommandText =
            $"UPDATE {method.DetailTable.TableName} SET {string.Join(", ", assignments)}"
            + $" WHERE {SampleKeyPredicate(method.DetailTable)};";
        AddSampleKeyParameters(command, sheetId, sampleId);
        command.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 删除一件试样的明细行（按 <c>(SheetID, SampleID)</c> 定位，与现役
    /// <c>DeleteRecord(ASheetID, ASampleID)</c> 同口径）。曲线、判定值、试样信息都随行一起没了。
    /// </summary>
    /// <returns>删到了行回 true；该件本来就不存在回 false（界面据此提示）。</returns>
    public Task<bool> DeleteSampleAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var method = ResolveMethodForSheet(sheetId);

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"DELETE FROM {method.DetailTable.TableName} WHERE {SampleKeyPredicate(method.DetailTable)};";
        AddSampleKeyParameters(command, sheetId, sampleId);

        return Task.FromResult(command.ExecuteNonQuery() > 0);
    }

    /// <summary>读某件试样的明细行（不存在返回 null）。</summary>
    public IReadOnlyDictionary<string, object?>? ReadDetailRow(
        string sheetId,
        string sampleId,
        IReadOnlyList<string> columns
    )
    {
        ArgumentNullException.ThrowIfNull(columns);

        var method = ResolveMethodForSheet(sheetId);

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {string.Join(", ", columns)} FROM {method.DetailTable.TableName}"
            + $" WHERE {SampleKeyPredicate(method.DetailTable)};";
        AddSampleKeyParameters(command, sheetId, sampleId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var row = new Dictionary<string, object?>(StringComparer.Ordinal);
        for (var i = 0; i < columns.Count; i++)
        {
            row[columns[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        }

        return row;
    }

    /// <summary>
    /// 存一件试样的信息（明细行即"试样记录"）：没有该行就插入，有就更新这些列（曲线与判定结果不动）。
    /// </summary>
    public Task UpsertSampleInfoAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, double> fields,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(fields);

        var method = ResolveMethodForSheet(sheetId);
        foreach (var name in fields.Keys)
        {
            if (!IsDeclared(method.DetailTable, name))
            {
                throw new ArgumentException(
                    $"方法 {method.MethodCode} 的明细表没有声明列 '{name}'",
                    nameof(fields)
                );
            }
        }

        using var connection = _initializer.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var probe = connection.CreateCommand())
        {
            probe.Transaction = transaction;
            probe.CommandText =
                $"SELECT COUNT(1) FROM {method.DetailTable.TableName}"
                + $" WHERE {SampleKeyPredicate(method.DetailTable)};";
            AddSampleKeyParameters(probe, sheetId, sampleId);

            var exists =
                Convert.ToInt64(probe.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
                > 0;

            if (exists)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                var assignments = fields
                    .Select((pair, index) => $"{pair.Key} = $p{index}")
                    .ToArray();
                update.CommandText =
                    $"UPDATE {method.DetailTable.TableName} SET {string.Join(", ", assignments)}"
                    + $" WHERE {SampleKeyPredicate(method.DetailTable)};";

                var i = 0;
                foreach (var value in fields.Values)
                {
                    update.Parameters.AddWithValue($"$p{i++}", value);
                }

                AddSampleKeyParameters(update, sheetId, sampleId);
                update.ExecuteNonQuery();
            }
            else
            {
                var values = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["SheetID"] = sheetId,
                    ["SampleID"] = sampleId,
                    ["CreateDate"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                };

                ApplySheetIndex(method.DetailTable, sampleId, values);

                foreach (var (name, value) in fields)
                {
                    values[name] = value;
                }

                // 明细第一次落地的另一条路径（只填试样信息、还没跑试验）：UUID 同样沿用所属单
                if (IsDeclared(method.DetailTable, UuidColumn))
                {
                    values[UuidColumn] = ResolveSheetUuid(
                        connection,
                        transaction,
                        method.MainTable,
                        sheetId
                    );
                }

                Insert(connection, transaction, method.DetailTable, values);
            }
        }

        transaction.Commit();
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
        // 排序：**按时间倒序**（最新的试验单在最上面；用户 2026-10-13 要求）。
        // `CreatedOn` 存的是 `yyyy-MM-dd HH:mm:ss` 文本，定长格式下文本序 = 时间序，可以直接 ORDER BY；
        // 同一秒建的单再按单号倒序（单号 = 前缀 + 日期 + 流水，倒序即新的在前）。
        command.CommandText = $"""
            SELECT m.SheetID,
                   m.ProjectName,
                   m.CreatedOn,
                   (SELECT COUNT(1) FROM {method.DetailTable.TableName} d WHERE d.SheetID = m.SheetID)
            FROM {method.MainTable.TableName} m
            WHERE ($keyword IS NULL OR m.SheetID LIKE $like OR m.ProjectName LIKE $like)
            ORDER BY m.CreatedOn DESC, m.SheetID DESC;
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

    /// <summary>读回一条明细的曲线（回放用）。定位口径与现役一致：单号 + 试件号。</summary>
    public IReadOnlyList<CurvePoint> ReadCurve(string sheetId, string sampleId)
    {
        var method = ResolveMethodForSheet(sheetId);

        using var connection = _initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT ChartData FROM {method.DetailTable.TableName}"
            + $" WHERE {SampleKeyPredicate(method.DetailTable)};";
        AddSampleKeyParameters(command, sheetId, sampleId);

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

    /// <summary>
    /// 明细行的定位条件：<c>SheetID + SampleID</c>（现役同口径 —— 现役的
    /// <c>GetRecord/DeleteRecord/GetChartData/UpdateChartData</c> 全是这两个参数）。
    /// 以前用自己发明的单列 `SampleKey`，用户 2026-10-13 要求去掉、与现役对齐。
    /// </summary>
    private static string SampleKeyPredicate(TableDefinition detailTable) =>
        $"SheetID = $sheet AND SampleID = $sample";

    /// <summary>给上面那条定位条件补参数（两个条件、两个参数）。</summary>
    private static void AddSampleKeyParameters(
        SqliteCommand command,
        string sheetId,
        string sampleId
    )
    {
        command.Parameters.AddWithValue("$sheet", sheetId);
        command.Parameters.AddWithValue("$sample", sampleId);
    }

    /// <summary>
    /// 明细行写入时按试件号回填 <c>SheetIndex</c>：试件号形如 `S001`，其中的序号就是 SheetIndex
    /// （现役同口径：`SampleID := 'S' + FormatFloat('000', SheetIndex)`）。
    /// 这样"下一个试件号"可以照现役那样取 `MAX(SheetIndex) + 1`，老记录（纯数字试件号）也认得。
    /// 试件号是人手填的怪值（取不出序号）就不写这一列，不猜。
    /// </summary>
    private static void ApplySheetIndex(
        TableDefinition detailTable,
        string sampleId,
        IDictionary<string, object?> values
    )
    {
        if (!IsDeclared(detailTable, SheetIndexColumn))
        {
            return;
        }

        if (SampleNumbering.ParseIndex(sampleId) is { } index)
        {
            values[SheetIndexColumn] = index;
        }
    }

    /// <summary>
    /// 取这张试验单的 UUID。**自愈**：老记录（本功能之前建的库）主表 UUID 是空的，
    /// 这时补一个并写回主表 —— 否则明细拿起 UUID 来会得到空串，云端两份数据对不上。
    /// 调用方已经开好连接与事务，这里的读写与明细插入同属一次事务。
    /// </summary>
    private static string ResolveSheetUuid(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TableDefinition mainTable,
        string sheetId
    )
    {
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                $"SELECT {UuidColumn} FROM {mainTable.TableName} WHERE {mainTable.PrimaryKeyColumn} = $key;";
            read.Parameters.AddWithValue("$key", sheetId);

            if (read.ExecuteScalar() is string existing && existing.Length > 0)
            {
                return existing;
            }
        }

        var uuid = RecordUuid.New();

        using (var write = connection.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText =
                $"UPDATE {mainTable.TableName} SET {UuidColumn} = $uuid WHERE {mainTable.PrimaryKeyColumn} = $key;";
            write.Parameters.AddWithValue("$uuid", uuid);
            write.Parameters.AddWithValue("$key", sheetId);
            write.ExecuteNonQuery();
        }

        return uuid;
    }

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
