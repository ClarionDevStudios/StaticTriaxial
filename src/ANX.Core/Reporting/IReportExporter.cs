namespace ANX.Core.Reporting;

/// <summary>
/// 报表导出。做法是**标签制**：模板里写占位符，代码按标签名填，不认单元格坐标
/// （现役按绝对坐标写入，"插一行就全错且不报错"）。
/// </summary>
public interface IReportExporter
{
    /// <summary>按请求导出一份报表文件。</summary>
    Task ExportAsync(ReportRequest request, CancellationToken cancellationToken = default);
}

/// <summary>一次报表导出请求。</summary>
/// <param name="MethodCode">方法代号。</param>
/// <param name="TemplatePath">标签制模板路径（按语种分套）。</param>
/// <param name="OutputPath">输出文件路径。</param>
/// <param name="Fields">单值标签：标签名 → 文本。</param>
/// <param name="DetailRows">明细行：每行一个"标签名 → 文本"字典。</param>
/// <param name="CurveImagePaths">
/// 曲线图文件（可选）。按顺序排在报表尾部：**两张一行、竖向往下**（用户 2026-10-15）。
/// </param>
public sealed record ReportRequest(
    string MethodCode,
    string TemplatePath,
    string OutputPath,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<IReadOnlyDictionary<string, string>> DetailRows,
    IReadOnlyList<string>? CurveImagePaths = null
);
