namespace STS.Ui.Converters;

using System.Globalization;
using System.Windows.Data;

/// <summary>
/// 日期字段在界面上用「日期选择器」，而字段值一律按 `yyyy-MM-dd` 文本存放（入库存文本、导出也直接可用）。
/// 这个转换器负责两个方向：文本 → <see cref="DateTime"/>（空串/非法值回 null）、日期 → 文本。
/// </summary>
public sealed class DateTextConverter : IValueConverter
{
    private const string Format = "yyyy-MM-dd";

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text
        && DateTime.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DateTime date
            ? date.ToString(Format, CultureInfo.InvariantCulture)
            : string.Empty;
}
