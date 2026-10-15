namespace ANX.Reporting;

using ANX.Core.Reporting;
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

    /// <inheritdoc />
    public Task ExportAsync(ReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var workbook = new XlsFile();
        workbook.Open(request.TemplatePath);

        var detailStartRow = -1;
        var detailLabelColumns = new Dictionary<string, int>(StringComparer.Ordinal);

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

                if (request.Fields.TryGetValue(label, out var value))
                {
                    workbook.SetCellValue(row, col, value);
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
