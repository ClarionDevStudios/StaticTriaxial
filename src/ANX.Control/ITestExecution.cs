namespace ANX.Control;

using ANX.Core;

/// <summary>实时指标卡的定义（界面按它生成卡片，方法只声明"我有哪些指标"）。</summary>
/// <param name="Key">指标键，与 <see cref="ITestExecution"/> 返回的实时值字典对应。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Unit">单位符号（无量纲留空）。</param>
/// <param name="ShowPeak">是否同时显示峰值。</param>
public sealed record MetricDefinition(
    string Key,
    string DisplayName,
    string Unit,
    bool ShowPeak = false
);

/// <summary>
/// 试验执行契约：界面靠它跑一轮试验，**不认识任何具体方法的算法与字段**
/// （T0715 的判定公式、库表、报表装配都留在方法工程里）。
/// </summary>
public interface ITestExecution
{
    /// <summary>方法代号。</summary>
    string MethodCode { get; }

    /// <summary>方法显示名。</summary>
    string MethodName { get; }

    /// <summary>依据的规范条款。</summary>
    string StandardReference { get; }

    /// <summary>运行参数与保护阈值（交给阶段机）。</summary>
    TestRunProfile Profile { get; }

    /// <summary>实时指标卡。</summary>
    IReadOnlyList<MetricDefinition> Metrics { get; }

    /// <summary>建一张新试验单，返回单号。</summary>
    Task<string> CreateSheetAsync(string projectName, string numberPrefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一个试件跑完：用曲线做判定并落库。
    /// </summary>
    /// <returns>判定出的指标（键与 <see cref="Metrics"/> 对应）；未结算时返回 null。</returns>
    Task<IReadOnlyDictionary<string, double>?> CompleteSampleAsync(
        string sheetId,
        string sampleId,
        IReadOnlyList<CurvePoint> curve,
        CancellationToken cancellationToken = default
    );

    /// <summary>一组跑完：算组统计并标记试验单完成。</summary>
    Task CompleteSheetAsync(string sheetId, CancellationToken cancellationToken = default);
}
