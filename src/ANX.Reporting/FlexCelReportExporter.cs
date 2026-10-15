namespace ANX.Reporting;

using ANX.Core.Reporting;
using FlexCel.Core;
using FlexCel.XlsAdapter;

/// <summary>
/// 标签制报表导出：模板里写占位符，代码按标签名填，**不认单元格坐标**。
///
/// 标签写法：`{{前缀.字段}}`，前缀沿用既定方案 ——
/// `Sheet.`（试验单级）/ `Param.`（参数）/ `Detail.`（试件明细行）/ `Result.`（组结果）/ `Image.`（图片）/ `Device.`（设备）。
///
/// 明细行的展开规则：模板里第一个含 `{{Detail.*}}` 的行是"明细模板行"，
/// 第 i 条明细写在 `起始行 + i` 上（模板行是第 0 条）。
/// **v1 限制**：第 1 条以后的明细行不复制模板行的格式 —— 只保证数据正确，格式复制留待后续。
/// </summary>
public sealed class FlexCelReportExporter : IReportExporter
{
    private const string LabelPrefix = "{{";
    private const string LabelSuffix = "}}";
    private const string DetailPrefix = "Detail.";

    /// <summary>
    /// 图片区标记：模板里带 `{{Images}}` 的那一行是**图片区标题行**，曲线图从它的下一行开始排。
    /// 这次没有曲线图时，把这一行到模板末尾**整段删掉**（连标题一起），不留一个空标题。
    /// </summary>
    private const string ImagesLabel = "Images";

    /// <summary>两张图一行（用户 2026-10-15 的口径："分 2 列，竖向排列"）。</summary>
    private const int ImagesPerRow = 2;

    /// <summary>
    /// 每张图占 4 列（0 基）：左图 A~D 从第 0 列起、右图 F~I 从第 5 列起，中间空 E 列当间隙。
    /// 左右两块的宽度接近（A~D ≈ 59.6 字符 / F~I ≈ 61.8 字符），所以两张图的尺寸基本一致。
    /// </summary>
    private static readonly int[] ImageColumnStarts = [0, 5];

    private const int ImageColumnSpan = 4;

    /// <summary>
    /// 每张图占 <see cref="ImageRows"/> 行、行高 <see cref="ImageRowHeightInTwips"/>（15pt），
    /// 即 14 × 15pt = 210pt ≈ 7.4cm 高；图宽 ≈ 11cm —— 与曲线图 900×600 的 3:2 接近，不会被拉变形。
    /// 两排之间留 <see cref="ImageGapRows"/> 行空隙。
    /// </summary>
    private const int ImageRows = 14;

    private const int ImageGapRows = 1;

    private const int ImageRowHeightInTwips = 300;

    /// <inheritdoc />
    public Task ExportAsync(ReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workbook = new XlsFile();
        workbook.Open(request.TemplatePath);

        var detailStartRow = -1;
        var detailLabelColumns = new Dictionary<string, int>(StringComparer.Ordinal);
        var imagesTitleRow = -1;

        var lastRow = workbook.RowCount;
        var lastCol = workbook.ColCount;

        for (var row = 1; row <= lastRow; row++)
        {
            for (var col = 1; col <= lastCol; col++)
            {
                if (workbook.GetCellValue(row, col) is not string text)
                {
                    continue;
                }

                var label = ExtractLabel(text);
                if (label is null)
                {
                    continue;
                }

                if (label.StartsWith(DetailPrefix, StringComparison.Ordinal))
                {
                    // 明细模板行：记下它和它每一列对应的字段名，稍后按行展开
                    detailStartRow = detailStartRow < 0 ? row : detailStartRow;
                    detailLabelColumns[label] = col;
                    continue;
                }

                if (string.Equals(label, ImagesLabel, StringComparison.Ordinal))
                {
                    // 图片区：只记位置，标记本身不印在报表上
                    imagesTitleRow = imagesTitleRow < 0 ? row : imagesTitleRow;
                    workbook.SetCellValue(row, col, string.Empty);
                    continue;
                }

                if (request.Fields.TryGetValue(label, out var value))
                {
                    workbook.SetCellValue(row, col, value);
                }
            }
        }

        // ── 明细行数不再有上限：模板里的明细块装不下就**插行** ──
        //    插行会把下面的说明区、签名与图片区一起推下去（Excel 的"插入行"语义，合并区也跟着走），
        //    所以"试样信息输出完毕后再输出剩余内容"是自然结果，不需要另写一套坐标。
        if (detailStartRow > 0)
        {
            var blockHeight = DetailBlockHeight(workbook, detailStartRow);
            if (request.DetailRows.Count > blockHeight)
            {
                var extra = request.DetailRows.Count - blockHeight;
                var blockEnd = detailStartRow + blockHeight - 1;

                // 插在明细块尾之后，复制块尾那一行（只有框、没有占位符）
                // 注意：只复制"一行的一部分"时 FlexCel 不复制**行高**，所以下面按明细行高补一遍
                var detailHeight = workbook.GetRowHeight(detailStartRow, HiddenIsZero: false);
                workbook.InsertAndCopyRange(
                    new TXlsCellRange(blockEnd, 1, blockEnd, lastCol),
                    blockEnd + 1,
                    1,
                    extra,
                    TFlxInsertMode.ShiftRowDown,
                    TRangeCopyMode.All
                );

                for (var row = blockEnd + 1; row <= blockEnd + extra; row++)
                {
                    workbook.SetRowHeight(row, detailHeight);
                }

                // 图片区（含标题行）跟着整体下移
                if (imagesTitleRow > 0)
                {
                    imagesTitleRow += extra;
                }
            }
        }

        if (detailStartRow > 0)
        {
            for (var i = 0; i < request.DetailRows.Count; i++)
            {
                var record = request.DetailRows[i];
                foreach (var (label, col) in detailLabelColumns)
                {
                    var text = record.TryGetValue(label, out var value) ? value : string.Empty;
                    workbook.SetCellValue(detailStartRow + i, col, text);
                }
            }
        }

        // ── 表尾曲线图（两张一行、竖向排列）──
        PlaceCurveImages(workbook, imagesTitleRow, request.CurveImagePaths, lastCol);

        var directory = Path.GetDirectoryName(Path.GetFullPath(request.OutputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 落盘走"临时文件 + 原子替换"：**FlexCel 的 `Save(路径)` 不允许覆盖已存在的文件**
        //（对已存在的目标会抛 IOException: The file ... already exists）—— 直接保存的话，
        // 同一张单导出第二次就会失败（用户会反复点"导出报表 / 打印报表"）。
        // 临时名带 Guid：上一次异常残留的 `.tmp` 也不会把它自己顶掉；Save 失败时不留半份报表。
        var temporary = $"{request.OutputPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            workbook.Save(temporary);
            File.Move(temporary, request.OutputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 模板里"明细块"有几行：从明细模板行往下数，**格式与它相同**的连续行都算块内
    /// （第一行格式不同的那一行就在块外 —— 我们的模板是空行 + 说明区）。
    /// 站点把明细块加长（多套几行同样格式）后容量自动跟着变大，不用改代码。
    /// </summary>
    private static int DetailBlockHeight(XlsFile workbook, int detailStartRow)
    {
        var detailFormat = workbook.GetCellFormat(detailStartRow, 1);
        var lastRow = workbook.RowCount;
        var height = 1;

        while (
            detailStartRow + height <= lastRow
            && workbook.GetCellFormat(detailStartRow + height, 1) == detailFormat
        )
        {
            height++;
        }

        return height;
    }

    /// <summary>
    /// 把曲线图排在表尾：**两张一行、竖向往下**排（用户 2026-10-15 的口径）。
    ///
    /// Excel 的图片是浮在网格上的对象，不是"往单元格里填值"，所以这里按行列**锚定**：
    /// 左图从第 0 列起占 4 列、右图从第 5 列起占 4 列，每排 14 行、行高 15pt、排与排之间留 1 行。
    /// 锚点用 <see cref="TFlxAnchorType.MoveAndResize"/>（Excel 的"随单元格移动并调整大小"），
    /// 并把 <c>Print</c> 打开 —— 否则打印 / 导 PDF 时图片不出现。
    ///
    /// <paramref name="imagesTitleRow"/> &lt;= 0 = 模板没标图片区：这次有图就从模板末尾往下排；
    /// 模板标了图片区但这次没图 → 把那一行到模板末尾**整段删掉**（连标题一起），不留空标题。
    /// </summary>
    private static void PlaceCurveImages(
        XlsFile workbook,
        int imagesTitleRow,
        IReadOnlyList<string>? curveImagePaths,
        int lastCol
    )
    {
        // 文件不在了就跳过这一张：图片是"上次跑完时存的"，被人删过也不该让整份报表导不出来
        var images = (curveImagePaths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .ToArray();

        if (imagesTitleRow <= 0)
        {
            if (images.Length == 0)
            {
                return;
            }

            // 模板没标图片区：接在末尾内容之后
            imagesTitleRow = workbook.RowCount + 1;
        }
        else if (images.Length == 0)
        {
            var lastRow = workbook.RowCount;
            if (lastRow >= imagesTitleRow)
            {
                workbook.DeleteRange(
                    new TXlsCellRange(imagesTitleRow, 1, lastRow, lastCol),
                    TFlxInsertMode.ShiftRowDown
                );
            }

            return;
        }

        var firstImageRow = imagesTitleRow + 1;

        for (var index = 0; index < images.Length; index++)
        {
            var slot = index / ImagesPerRow;
            var topRow = firstImageRow + (slot * (ImageRows + ImageGapRows));
            var firstCol = ImageColumnStarts[index % ImagesPerRow];

            // 行高显式设一遍：图片的高度是按"行高之和"算的，靠默认行高排出来会忽高忽低
            for (var row = topRow; row < topRow + ImageRows + ImageGapRows; row++)
            {
                workbook.SetRowHeight(row, ImageRowHeightInTwips);
            }

            // 锚点行列是 **1 基**（踩过：按 xlsx 里 drawing 的 0 基写，FlexCel 会抛
            // "Invalid column index: -1"）；dx/dy 都给 0，图正好落在格子边界上
            var anchor = new TClientAnchor(
                TFlxAnchorType.MoveAndResize,
                topRow,
                0,
                firstCol + 1,
                0,
                topRow + ImageRows,
                0,
                firstCol + 1 + ImageColumnSpan,
                0
            );

            workbook.AddImage(
                images[index],
                new TImageProperties(anchor, images[index]) { Print = true }
            );
        }

        // ── 让每排曲线图完整落在一页里（用户 2026-10-15：按打印设置分页渲染）──
        //    自动分页是按行数硬切的，而浮动图片一旦跨过页边界，**前后两页会各画一遍**（实测确认），
        //    所以要在每排图的**最后一行之下**插分页符（`aGoesAfter: true` 那个重载）。
        //    曲线区也从新一页开始，表格页干净。
        var pitch = ImageRows + ImageGapRows;
        var pairCount = (images.Length + ImagesPerRow - 1) / ImagesPerRow;
        for (var pair = 1; pair < pairCount; pair++)
        {
            var lastRowOfPair = firstImageRow + ((pair - 1) * pitch) + ImageRows - 1;
            workbook.InsertHPageBreak(lastRowOfPair, aGoesAfter: true);
        }
    }

    /// <summary>从单元格文本里取出标签名；不是标签返回 null。</summary>
    private static string? ExtractLabel(string text)
    {
        var start = text.IndexOf(LabelPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = text.IndexOf(LabelSuffix, start + LabelPrefix.Length, StringComparison.Ordinal);
        return end < 0 ? null : text[(start + LabelPrefix.Length)..end].Trim();
    }
}
