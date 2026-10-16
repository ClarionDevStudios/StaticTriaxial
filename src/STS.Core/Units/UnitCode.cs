namespace STS.Core.Units;

/// <summary>
/// 量纲码。与现役 <c>ProtocolTypes.pas</c> 的 <c>UNIT_CODE_*</c> 常量一一对应，便于与现役抓帧对照。
/// </summary>
public enum UnitCode
{
    None = 0,
    Kilonewton = 1,
    Newton = 2,
    Centimeter = 3,
    Millimeter = 4,
    Second = 5,
    Kilopascal = 6,
    Megapascal = 7,
    MillimeterPerSecond = 8,
    RevolutionsPerMinute = 9,
}

/// <summary>量纲码与显示符号之间的换算。显示符号集中在这里，不散落到界面与报表。</summary>
public static class UnitCodes
{
    /// <summary>返回量纲的显示符号；<see cref="UnitCode.None"/> 返回空串。</summary>
    public static string ToSymbol(this UnitCode code) =>
        code switch
        {
            UnitCode.None => string.Empty,
            UnitCode.Kilonewton => "kN",
            UnitCode.Newton => "N",
            UnitCode.Centimeter => "cm",
            UnitCode.Millimeter => "mm",
            UnitCode.Second => "s",
            UnitCode.Kilopascal => "kPa",
            UnitCode.Megapascal => "MPa",
            UnitCode.MillimeterPerSecond => "mm/min",
            UnitCode.RevolutionsPerMinute => "rpm",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "未知量纲码"),
        };

    /// <summary>按显示符号反查量纲码。</summary>
    public static bool TryParseSymbol(string? symbol, out UnitCode code)
    {
        foreach (var candidate in Enum.GetValues<UnitCode>())
        {
            if (candidate == UnitCode.None)
            {
                continue;
            }

            if (string.Equals(candidate.ToSymbol(), symbol?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                code = candidate;
                return true;
            }
        }

        code = UnitCode.None;
        return false;
    }
}
