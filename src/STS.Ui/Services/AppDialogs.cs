namespace STS.Ui.Services;

using System.Windows;
using System.Windows.Controls;
using UiMessageBox = Wpf.Ui.Controls.MessageBox;
using UiMessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

/// <summary>
/// 应用内提示框的**统一构造**：尺寸大、字大、居中对齐。
///
/// 为什么不能只用 WPF-UI 的默认样子：`Wpf.Ui.Controls.MessageBox` 按内容自适应，而它的样式里
/// 写死了 <c>MaxWidth = 450</c>、正文 14 号（翻库里的 `MessageBox.xaml` 确认过）—— 现场工控机前
/// 站着看，一行字挤成一条（用户 2026-10-15："把提示窗做大一些"）。
///
/// 做法：把正文换成**有最小尺寸**的 <see cref="TextBlock"/>（字号 <see cref="BodyFontSize"/>、
/// 居中、自动折行），同时放宽 <c>MaxWidth</c> —— 对话框按内容自适应时就会取到这个尺寸。
/// 想整体再大/再小，只改下面三个常量。
/// </summary>
public static class AppDialogs
{
    /// <summary>提示框正文的宽度（DIP）。</summary>
    public const double BodyWidth = 520;

    /// <summary>提示框正文的最小高度（DIP）—— 一句话的提示也留出这块空白，看着不挤。</summary>
    public const double BodyMinHeight = 130;

    /// <summary>提示框正文字号。</summary>
    public const double BodyFontSize = 18;

    /// <summary>
    /// 造一个尺寸统一的提示框。<paramref name="secondaryButton"/> 不给就只有主按钮；
    /// 标题栏的 X 一律关掉 —— 关窗回 <c>None</c>，与"没点确定"同义，调用方不用区分。
    /// </summary>
    public static UiMessageBox Create(
        string title,
        string body,
        string primaryButton,
        string? secondaryButton = null
    )
    {
        return new UiMessageBox
        {
            Title = title,
            Content = new TextBlock
            {
                Text = body,
                FontSize = BodyFontSize,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = BodyWidth,
                MinHeight = BodyMinHeight,
            },
            PrimaryButtonText = primaryButton,
            SecondaryButtonText = secondaryButton ?? string.Empty,
            IsCloseButtonEnabled = false,
            // 放宽库样式里的 450：正文宽度 + 两侧留白（模板给 ContentPresenter 12+12）
            MaxWidth = BodyWidth + 80,
            Owner = Application.Current?.MainWindow,
        };
    }

    /// <summary>问一句"要不要"：返回 <c>true</c> = 用户点了主按钮（确定 / 是）。</summary>
    public static async Task<bool> ConfirmAsync(
        string title,
        string body,
        string confirmButton,
        string cancelButton
    ) => await Create(title, body, confirmButton, cancelButton).ShowDialogAsync()
        == UiMessageBoxResult.Primary;

    /// <summary>只报一句（一个"关闭 / 知道了"按钮）。</summary>
    public static async Task NotifyAsync(string title, string body, string closeButton) =>
        await Create(title, body, closeButton).ShowDialogAsync();
}
