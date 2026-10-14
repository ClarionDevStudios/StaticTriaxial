namespace ANX.Storage;

using ANX.Core.TestSheets;
using Microsoft.Data.Sqlite;

/// <summary>
/// 单号流水：`前缀 + yyyyMMdd + 3 位流水`，当天从 001 起递增（沿用现役口径）。
/// 流水存在框架表 <see cref="SchemaBuilder.SheetSequenceTable"/> 里，同一天同一前缀只发一次。
/// </summary>
public sealed class SheetNumberGenerator(DatabaseInitializer initializer)
{
    /// <summary>
    /// **预览**当天该前缀的下一个单号：只读流水、**不消耗**（用户 2026-10-14：新建时单号只读显示）。
    /// 真正发号仍走 <see cref="Next"/>；若两次调用之间有人建了单，预览值可能过时，
    /// 所以界面在**保存成功后**要用创建接口返回的真实单号刷新显示。
    /// </summary>
    public SheetNumber Peek(string prefix, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("单号前缀不能为空", nameof(prefix));
        }

        var dateKey = date.ToString("yyyyMMdd");

        using var connection = initializer.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT LastSequence FROM {SchemaBuilder.SheetSequenceTable} WHERE Prefix = $prefix AND SheetDate = $date;";
        command.Parameters.AddWithValue("$prefix", prefix);
        command.Parameters.AddWithValue("$date", dateKey);

        var value = command.ExecuteScalar();
        var last = value is null or DBNull ? 0 : Convert.ToInt32(value);

        var next = new SheetNumber(prefix, date, last).NextSequence();
        if (next == SheetNumber.SequenceExhausted)
        {
            throw new InvalidOperationException($"单号流水已用尽（{prefix} {dateKey} 当天超过 999 个）。");
        }

        return new SheetNumber(prefix, date, next);
    }

    /// <summary>取当天该前缀的下一个单号。</summary>
    public SheetNumber Next(string prefix, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("单号前缀不能为空", nameof(prefix));
        }

        var dateKey = date.ToString("yyyyMMdd");

        using var connection = initializer.OpenConnection();
        using var transaction = connection.BeginTransaction();

        int last;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText =
                $"SELECT LastSequence FROM {SchemaBuilder.SheetSequenceTable} WHERE Prefix = $prefix AND SheetDate = $date;";
            read.Parameters.AddWithValue("$prefix", prefix);
            read.Parameters.AddWithValue("$date", dateKey);

            var value = read.ExecuteScalar();
            last = value is null or DBNull ? 0 : Convert.ToInt32(value);
        }

        var next = new SheetNumber(prefix, date, last).NextSequence();
        if (next == SheetNumber.SequenceExhausted)
        {
            throw new InvalidOperationException($"单号流水已用尽（{prefix} {dateKey} 当天超过 999 个）。");
        }

        using (var write = connection.CreateCommand())
        {
            write.Transaction = transaction;
            write.CommandText = $"""
                INSERT INTO {SchemaBuilder.SheetSequenceTable} (Prefix, SheetDate, LastSequence)
                VALUES ($prefix, $date, $sequence)
                ON CONFLICT (Prefix, SheetDate) DO UPDATE SET LastSequence = $sequence;
                """;
            write.Parameters.AddWithValue("$prefix", prefix);
            write.Parameters.AddWithValue("$date", dateKey);
            write.Parameters.AddWithValue("$sequence", next);
            write.ExecuteNonQuery();
        }

        transaction.Commit();
        return new SheetNumber(prefix, date, next);
    }
}
