namespace STS.Method.ShearStrength;

using STS.Control;
using STS.Core;
using STS.Core.TestSheets;

/// <summary>历史浏览 / 曲线回放（骨架占位：先回空；周内接库）。</summary>
public sealed class ShearStrengthSheetBrowser : ITestSheetBrowser
{
    public string CurveXAxisKey => "Chart.Axis.Deformation";

    public string CurveYAxisKey => "Chart.Axis.Load";

    public Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<TestSheetSummary>>([]);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadSamplesAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([]);

    public Task<IReadOnlyList<CurvePoint>> ReadCurveAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<CurvePoint>>([]);

    public Task<CurveAnnotation?> ReadAnnotationAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<CurveAnnotation?>(null);
}
