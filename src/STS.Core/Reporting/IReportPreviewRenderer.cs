namespace STS.Core.Reporting;

/// <summary>
/// 把一份**已生成的报表文件**渲染成图，供界面做"打印预览"（用户 2026-10-15）。
///
/// 契约放在 Core 里，是因为界面工程只引用 Core / Control（不引用具体的报表实现工程）——
/// 实现见 `STS.Reporting.FlexCelReportPreviewRenderer`，由宿主把它接到报表侧。
/// </summary>
public interface IReportPreviewRenderer
{
    /// <summary>
    /// 把报表的**可见内容**渲成一张 PNG（整张表一张图，含嵌在表尾的曲线图）。
    /// 隐藏列（出厂模板在隐藏列里藏着版本戳）不进图。
    /// </summary>
    /// <param name="reportPath">已经生成好的报表文件（.xlsx）。</param>
    /// <param name="dpi">渲染分辨率；150 够屏幕预览，打印也够用。</param>
    byte[] RenderPng(string reportPath, double dpi = 150);
}
