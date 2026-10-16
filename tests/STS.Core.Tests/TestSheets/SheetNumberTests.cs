namespace STS.Core.Tests.TestSheets;

using STS.Core.TestSheets;

/// <summary>试验单号：前缀 + yyyyMMdd + 3 位流水。</summary>
public sealed class SheetNumberTests
{
    private static readonly DateOnly SampleDate = new(2026, 10, 10);

    [Fact]
    public void 格式化应为前缀加八位日期加三位流水()
    {
        var number = new SheetNumber("WT", SampleDate, 7);

        Assert.Equal("WT20261010007", number.ToString());
    }

    [Fact]
    public void 解析应与格式化往返一致()
    {
        var original = new SheetNumber("TST1-", SampleDate, 123);

        Assert.True(SheetNumber.TryParse(original.ToString(), out var parsed));
        Assert.Equal(original, parsed);
        Assert.Equal("TST1-20261010123", parsed.ToString());
    }

    [Fact]
    public void 流水号为零与最大值都应能解析()
    {
        Assert.True(SheetNumber.TryParse("WT20261010000", out var zero));
        Assert.Equal(0, zero.Sequence);

        Assert.True(SheetNumber.TryParse("WT20261010999", out var max));
        Assert.Equal(SheetNumber.MaxSequence, max.Sequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("WT2026101")] // 太短
    [InlineData("20261010001")] // 没有前缀
    [InlineData("WT20261301001")] // 月份非法
    [InlineData("WT2026101000A")] // 尾部不是数字
    public void 非法单号应解析失败(string? text)
    {
        Assert.False(SheetNumber.TryParse(text, out var parsed));
        Assert.Equal(default, parsed);
    }

    [Fact]
    public void 下一个流水号应递增当天流水用尽时报告()
    {
        var number = new SheetNumber("WT", SampleDate, 0);
        Assert.Equal(1, number.NextSequence());

        var last = new SheetNumber("WT", SampleDate, SheetNumber.MaxSequence);
        Assert.Equal(SheetNumber.SequenceExhausted, last.NextSequence());
    }
}
