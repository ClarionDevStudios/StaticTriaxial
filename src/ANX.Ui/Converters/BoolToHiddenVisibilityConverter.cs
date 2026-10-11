namespace ANX.Ui.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>
/// true → <see cref="Visibility.Visible"/>，false → <see cref="Visibility.Hidden"/>（**不是** Collapsed）。
/// 用途：让某一行"占位但不可见" —— 例如指标卡里的"峰值"行：没有峰值的卡也保留这一行的高度，
/// 于是一排卡片的高度由内容统一决定，不必写死 `Height` / `MinHeight`。
/// </summary>
public sealed class BoolToHiddenVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Hidden;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("只用于单向绑定。");
}
