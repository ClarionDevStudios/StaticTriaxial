namespace ANX.Core.Tests.Units;

using ANX.Core.Units;

/// <summary>量纲码与显示符号。重点是"码值与现役常量一致"，因为抓帧对照时要靠它对齐。</summary>
public sealed class UnitCodeTests
{
    [Fact]
    public void 码值应与现役ProtocolTypes的量纲常量一致()
    {
        Assert.Equal(0, (int)UnitCode.None);
        Assert.Equal(1, (int)UnitCode.Kilonewton);
        Assert.Equal(2, (int)UnitCode.Newton);
        Assert.Equal(3, (int)UnitCode.Centimeter);
        Assert.Equal(4, (int)UnitCode.Millimeter);
        Assert.Equal(5, (int)UnitCode.Second);
        Assert.Equal(6, (int)UnitCode.Kilopascal);
        Assert.Equal(7, (int)UnitCode.Megapascal);
        Assert.Equal(8, (int)UnitCode.MillimeterPerSecond);
        Assert.Equal(9, (int)UnitCode.RevolutionsPerMinute);
    }

    [Fact]
    public void 每个量纲的符号应能与码值往返()
    {
        foreach (var code in Enum.GetValues<UnitCode>())
        {
            if (code == UnitCode.None)
            {
                continue;
            }

            var symbol = code.ToSymbol();
            Assert.False(string.IsNullOrWhiteSpace(symbol));
            Assert.True(UnitCodes.TryParseSymbol(symbol, out var parsed), $"符号 {symbol} 应能反查");
            Assert.Equal(code, parsed);
        }
    }

    [Fact]
    public void 无量纲的符号是空串且不可反查()
    {
        Assert.Equal(string.Empty, UnitCode.None.ToSymbol());
        Assert.False(UnitCodes.TryParseSymbol(string.Empty, out var parsed));
        Assert.Equal(UnitCode.None, parsed);
    }

    [Fact]
    public void 未知符号应反查失败()
    {
        Assert.False(UnitCodes.TryParseSymbol("kg", out _));
        Assert.False(UnitCodes.TryParseSymbol(null, out _));
    }

    [Fact]
    public void 未知码值求符号应抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((UnitCode)99).ToSymbol());
    }
}
