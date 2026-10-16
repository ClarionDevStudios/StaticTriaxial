namespace STS.Core.Tests;

using STS.Core;

/// <summary>
/// 试件号口径（用户 2026-10-13）：`S` + 三位序号，与现役 `SampleID := 'S' + FormatFloat('000', SheetIndex)` 一致。
/// </summary>
public sealed class SampleNumberingTests
{
    [Theory]
    [InlineData(1, "S001")]
    [InlineData(9, "S009")]
    [InlineData(10, "S010")]
    [InlineData(99, "S099")]
    [InlineData(100, "S100")]
    [InlineData(1000, "S1000")] // 超过三位不截断（现役 FormatFloat('000') 同行为）
    public void 按三位序号拼试件号(int index, string expected) =>
        Assert.Equal(expected, SampleNumbering.Format(index));

    [Theory]
    [InlineData("S001", 1)]
    [InlineData("S123", 123)]
    [InlineData("s7", 7)]   // 小写也认
    [InlineData("12", 12)]  // 老记录是纯数字
    public void 从试件号取回序号(string sampleId, int expected) =>
        Assert.Equal(expected, SampleNumbering.ParseIndex(sampleId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("S")]
    [InlineData("S0")]
    [InlineData("SX")]
    [InlineData("试件一")]
    public void 取不出序号时回null(string? sampleId) =>
        Assert.Null(SampleNumbering.ParseIndex(sampleId));
}
