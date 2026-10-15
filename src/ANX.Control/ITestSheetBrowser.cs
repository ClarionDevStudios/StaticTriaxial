namespace ANX.Control;

using ANX.Core;
using ANX.Core.TestSheets;

/// <summary>
/// 试验单浏览契约（历史数据页用）：只回"列名 → 文本"的通用行，界面不认识任何具体字段。
/// </summary>
public interface ITestSheetBrowser : ICurveAxisSource
{
    /// <summary>按关键字查试验单；<paramref name="keyword"/> 为空时返回全部。</summary>
    Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 读某张试验单的试件明细（每行一个"**列名的显示资源键** → 文本"）。
    /// 键是资源键而不是库里的列名（例：<c>Method.T0715.PeakLoad</c>）：界面用 <c>L(key)</c> 取当前语言
    /// 的列名（用户 2026-10-15：列表里的 <c>SampleID</c>、<c>PeakLoadValue</c> 这种库列名要跟着语言走），
    /// 这样界面仍然不认识任何具体字段 —— 列名只在方法工程里出现。
    /// </summary>
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

    /// <summary>
    /// 读某个试件的**曲线标注**信息（修正直线 / 修正原点 / 拟合区间百分比），
    /// 供历史数据页把回放图画成与试验执行页同样的样子（用户 2026-10-13：两处保持一致）。
    ///
    /// 没有拟合的试件（关掉了原点修正、或判定失败、或老记录没存拟合列）回 <c>null</c> ——
    /// 界面据此只画峰值点。
    /// </summary>
    Task<CurveAnnotation?> ReadAnnotationAsync(
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

    /// <summary>
    /// **只把某一件试件的曲线图**写到产物目录（`Report\弯曲\<单号>\<试样号>.png`），**不出整份报表**。
    /// 用途：**试验完成后自动存图**（用户 2026-10-13/2026-10-14 的原始口径）。
    /// 同名覆盖 —— 重跑同一件以最后一次为准。
    /// </summary>
    /// <returns>图片路径；这件没有曲线数据（空件 / 未接触）时返回 <c>null</c>。</returns>
    Task<string?> SaveCurveImageAsync(
        string sheetId,
        string sampleId,
        string culture,
        CancellationToken cancellationToken = default
    );
}
