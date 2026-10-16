namespace STS.Core.TestSheets;

/// <summary>
/// 试验单生命周期与查询。框架负责单号生成、生命周期与查询装配；
/// 方法只通过自己的持久化模块声明表与列，框架里不出现任何具体方法字段。
/// </summary>
public interface ITestSheetService
{
    /// <summary>建一张新试验单，返回单号。</summary>
    Task<string> CreateAsync(
        string methodCode,
        string projectName,
        string numberPrefix,
        CancellationToken cancellationToken = default
    );

    /// <summary>往试验单里追加一个试件（含曲线与判定指标）。</summary>
    Task AppendSampleAsync(
        string sheetId,
        string sampleId,
        IReadOnlyList<CurvePoint> curve,
        IReadOnlyDictionary<string, double> results,
        CancellationToken cancellationToken = default
    );

    /// <summary>把试验单标记为完成。</summary>
    Task CompleteAsync(string sheetId, CancellationToken cancellationToken = default);

    /// <summary>按方法查试验单列表；<paramref name="keyword"/> 为空时不加检索条件。</summary>
    Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string methodCode,
        string? keyword = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>试验单列表页用的一行摘要。</summary>
public sealed record TestSheetSummary(
    string SheetId,
    string MethodCode,
    string ProjectName,
    DateOnly CreatedOn,
    int SampleCount
);
