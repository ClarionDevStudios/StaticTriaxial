namespace STS.Core;

using System.Globalization;

/// <summary>
/// 试件号（<c>SampleID</c>）的口径：**`S` + 三位序号**（S001、S002…），与现役一致 ——
/// 现役同口径：<c>SampleID := 'S' + FormatFloat('000', SheetIndex)</c>，
/// 序号取"该单已有的最大 SheetIndex + 1"（单里还没有试件时为 1）。
///
/// 序号就是明细行的 <c>SheetIndex</c>：试件号与它是同一个数，两边不会各说各话。
/// 超过三位不截断（第 1000 件是 S1000）—— 现役的 <c>FormatFloat('000', …)</c> 也是这个行为。
/// </summary>
public static class SampleNumbering
{
    /// <summary>序号最少几位（不足补零）。</summary>
    public const int MinimumDigits = 3;

    /// <summary>按序号拼试件号：1 → <c>S001</c>。</summary>
    public static string Format(int index) =>
        "S" + index.ToString(new string('0', MinimumDigits), CultureInfo.InvariantCulture);

    /// <summary>
    /// 从试件号里取回序号：<c>S005</c> → 5，<c>5</c> → 5（老记录是纯数字）。
    /// 取不出来回 <c>null</c>（人工填的奇怪值不该把序号算歪）。
    /// </summary>
    public static int? ParseIndex(string? sampleId)
    {
        var text = sampleId?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (text[0] is 'S' or 's')
        {
            text = text[1..];
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index > 0
            ? index
            : null;
    }
}
