namespace ANX.Core.TestSheets;

using System.Globalization;

/// <summary>
/// 试验单号。规则（沿用现役口径）：<c>前缀 + yyyyMMdd + 3 位流水</c>，例 <c>WT20261010001</c>。
/// </summary>
public readonly record struct SheetNumber(string Prefix, DateOnly Date, int Sequence)
{
    /// <summary>流水号位数。</summary>
    public const int SequenceDigits = 3;

    /// <summary>流水号上限（999）。</summary>
    public const int MaxSequence = 999;

    /// <summary>当天流水用尽。</summary>
    public const int SequenceExhausted = -1;

    /// <summary>格式化为单号文本。</summary>
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{Date:yyyyMMdd}{Sequence.ToString($"D{SequenceDigits}", CultureInfo.InvariantCulture)}"
        );

    /// <summary>解析单号文本。前缀不能为空，尾部必须是 8 位日期 + 3 位流水。</summary>
    public static bool TryParse(string? text, out SheetNumber value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(text) || text.Length <= 11)
        {
            return false;
        }

        var tail = text.AsSpan(text.Length - 11);
        foreach (var ch in tail)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        var prefix = text[..^11];
        if (prefix.Length == 0)
        {
            return false;
        }

        if (
            !DateOnly.TryParseExact(
                tail[..8],
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date
            )
        )
        {
            return false;
        }

        var sequence = int.Parse(tail[8..], CultureInfo.InvariantCulture);
        value = new SheetNumber(prefix, date, sequence);
        return true;
    }

    /// <summary>
    /// 求同一天的下一个单号。当天流水用尽时返回 <see cref="SequenceExhausted"/>。
    /// </summary>
    public int NextSequence() =>
        Sequence >= MaxSequence ? SequenceExhausted : Sequence + 1;
}
