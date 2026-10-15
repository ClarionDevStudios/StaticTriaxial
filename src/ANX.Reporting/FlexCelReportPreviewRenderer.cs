namespace ANX.Reporting;

using ANX.Core.Reporting;
using FlexCel.Core;
using FlexCel.XlsAdapter;

/// <summary>
/// 报表预览渲染：用 FlexCel **自带**的渲染器把整张表渲成一张 PNG。
///
/// 注意（2026-10-15 更正过一条旧结论）：渲染能力**就在 `FlexCel.dll` 里**，不需要单独的
/// `FlexCel.Render` 包 —— 零售包的 `lib\` 只有 .NET Framework，但 `.nupkg` 的 net10.0 那一份
/// 核心程序集里就带着 `FlexCel.Render.*` 与 `XlsFile.RenderCells`。
///
/// **不做分页**（用户 2026-10-15 定）：整张表一张长图，界面按宽度铺满、居中显示。
/// 分页能力（`FlexCel.Render.FlexCelImgExport`）仍在，将来要做"按页预览/导 PDF"时可复用。
/// </summary>
public sealed class FlexCelReportPreviewRenderer : IReportPreviewRenderer
{
    /// <inheritdoc />
    public byte[] RenderPng(string reportPath, double dpi = 150)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);

        var workbook = new XlsFile();
        workbook.Open(reportPath);

        // 取"实际有内容、且没被隐藏"的范围：模板把版本戳放在隐藏列 K 里、J 列是隐藏的隔离列，
        // 两列都不该渲进图里（否则图右边会多出一条空白带）
        var lastRow = workbook.RowCount;
        var lastCol = workbook.ColCount;
        while (lastCol > 1 && workbook.GetColHidden(lastCol))
        {
            lastCol--;
        }

        var image = workbook.RenderCells(
            1,
            1,
            lastRow,
            lastCol,
            drawBackground: true,
            dpi,
            TUISmoothingMode.HighQuality,
            TUIInterpolationMode.HighQualityBicubic,
            antiAliased: true,
            aPrintScale: 1.0,
            forceBackgroundTransparent: false,
            exportObjects: true // 把嵌在表尾的曲线图一起渲进去
        );

        using var stream = new MemoryStream();
        _ = image.Save(stream, TXlsImgType.Png);
        return stream.ToArray();
    }
}
