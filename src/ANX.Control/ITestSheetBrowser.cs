namespace ANX.Control;

using ANX.Core;
using ANX.Core.TestSheets;

/// <summary>
/// 试验单浏览契约（历史数据页用）：只回"列名 → 文本"的通用行，界面不认识任何具体字段。
/// </summary>
public interface ITestSheetBrowser
{
    /// <summary>按关键字查试验单；<paramref name="keyword"/> 为空时返回全部。</summary>
    Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>读某张试验单的试件明细（每行一个"列名 → 文本"）。</summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadSamplesAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>读某个试件的曲线（回放 / 预览用）。</summary>
    Task<IReadOnlyList<CurvePoint>> ReadCurveAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>报表导出契约：界面只说"导出这张试验单"，模板选择与填标签都留在方法工程里。</summary>
public interface IReportExportService
{
    /// <summary>导出指定试验单，返回产物路径。</summary>
    Task<string> ExportAsync(
        string sheetId,
        string culture,
        CancellationToken cancellationToken = default
    );
}
