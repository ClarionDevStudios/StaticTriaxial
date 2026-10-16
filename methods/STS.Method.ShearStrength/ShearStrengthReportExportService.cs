namespace STS.Method.ShearStrength;

using STS.Control;
using STS.Core.Reporting;
using STS.Storage;

/// <summary>
/// 报表导出（骨架占位）：契约先接上，实际导出（FlexCel 标签制模板、照现役实物、中英两套）周内实现。
/// </summary>
#pragma warning disable CS9113 // 骨架：依赖先接上，周内实现导出时使用
public sealed class ShearStrengthReportExportService(
    TestSheetService sheets,
    string reportRoot,
    ITextResolver texts,
    ShearStrengthParameters parameters
) : IReportExportService
#pragma warning restore CS9113
{
    public Task<string> ExportAsync(
        string sheetId,
        string culture,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public Task<string?> SaveCurveImageAsync(
        string sheetId,
        string sampleId,
        string culture,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    private static NotSupportedException Skeleton() =>
        new("静三轴报表导出还在骨架阶段：周内照现役实物实现（标签制模板 + 中英两套）。");
}
