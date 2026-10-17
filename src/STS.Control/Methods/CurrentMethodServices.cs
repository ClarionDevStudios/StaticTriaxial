namespace STS.Control.Methods;

using STS.Core;
using STS.Core.Control;
using STS.Core.TestSheets;

/// <summary>
/// **执行契约的"当前方法"代理**：每次调用都转给 <see cref="ICurrentMethodContext.Current"/>。
///
/// 为什么不直接把方法实现注册成单例：DI 的单例工厂**在第一次解析时就把实例定死了**——
/// 换方法后页面手里还是旧方法那一个。走一层代理，页面持有的东西一直不变、但每次调用落到当前方法上
///（与 IAP 的"当前方法上下文 + 解析工厂"同款口径）。
///
/// 同时实现 <see cref="ITestExecution"/> 与 <see cref="ITestSheetEditor"/>：
/// 方法工程里本来就是同一个实例扮演这两个角色（见方法工程里的执行类），这里照旧。
/// **界面仍需自己订 <see cref="ICurrentMethodContext.Changed"/>**：代理只保证"以后调用取新方法"，
/// 已经画在界面上的卡片 / 列 / 单号是页面自己的状态，得由页面重建。
/// </summary>
/// <remarks>
/// 构造参数是 **<see cref="Func{TResult}"/> 而不是上下文本身**：目录里的方法服务（例如
/// 方法工程里的浏览件）也依赖本代理，而目录又由上下文构造 —— 直接要上下文就成环
/// （实测：首页一显示就卡住启动导航）。用函数懒取，**构造期不碰上下文**，只在实际调用时解析。
/// </remarks>
public sealed class CurrentMethodExecution(Func<ICurrentMethodContext> methods)
    : ITestExecution, ITestSheetEditor
{
    private ITestExecution Execution => methods().Current.Execution;

    private ITestSheetEditor Editor => methods().Current.Editor;

    public string MethodCode => Execution.MethodCode;

    public string MethodName => Execution.MethodName;

    public string MethodNameKey => Execution.MethodNameKey;

    public string StandardReference => Execution.StandardReference;

    public string CurveXAxisKey => Execution.CurveXAxisKey;

    public string CurveYAxisKey => Execution.CurveYAxisKey;

    public TestRunProfile Profile => Execution.Profile;

    public IReadOnlyList<MetricDefinition> Metrics => Execution.Metrics;

    public IReadOnlyList<ParameterDefinition> SampleFields => Execution.SampleFields;

    public Task<TestRunProfile> LoadProfileAsync(
        TestStartContext? context = null,
        CancellationToken cancellationToken = default
    ) => Execution.LoadProfileAsync(context, cancellationToken);

    public Task<IReadOnlyDictionary<string, string>> LoadSampleInfoAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Execution.LoadSampleInfoAsync(sheetId, sampleId, cancellationToken);

    public Task<IReadOnlyList<SampleRowInfo>> ReadSampleRowsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Execution.ReadSampleRowsAsync(sheetId, cancellationToken);

    public Task SaveSampleInfoAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => Execution.SaveSampleInfoAsync(sheetId, sampleId, fields, cancellationToken);

    public Task<string> CreateSheetAsync(
        string projectName,
        string numberPrefix,
        CancellationToken cancellationToken = default
    ) => Execution.CreateSheetAsync(projectName, numberPrefix, cancellationToken);

    public Task<string> NextSampleIdAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Execution.NextSampleIdAsync(sheetId, cancellationToken);

    public Task<bool> DeleteSampleAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Execution.DeleteSampleAsync(sheetId, sampleId, cancellationToken);

    public Task<IReadOnlyDictionary<string, double>?> CompleteSampleAsync(
        string sheetId,
        string sampleId,
        IReadOnlyList<CurvePoint> curve,
        CancellationToken cancellationToken = default
    ) => Execution.CompleteSampleAsync(sheetId, sampleId, curve, cancellationToken);

    public Task<SheetFinishSummary> CompleteSheetAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Execution.CompleteSheetAsync(sheetId, cancellationToken);

    public Task<ValidityReport> EvaluateValidityAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Execution.EvaluateValidityAsync(sheetId, cancellationToken);

    public Task<RecomputeReport> RecomputeSheetAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Execution.RecomputeSheetAsync(sheetId, cancellationToken);

    // ── ITestSheetEditor ────────────────────────────────────────────────

    public IReadOnlyList<ParameterDefinition> SheetInfoFields => Editor.SheetInfoFields;

    public Task<string> PeekNextSheetIdAsync(CancellationToken cancellationToken = default) =>
        Editor.PeekNextSheetIdAsync(cancellationToken);

    public Task<IReadOnlyDictionary<string, string>> ReadSheetInfoAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Editor.ReadSheetInfoAsync(sheetId, cancellationToken);

    public Task<string> CreateSheetAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => Editor.CreateSheetAsync(fields, cancellationToken);

    public Task UpdateSheetAsync(
        string sheetId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => Editor.UpdateSheetAsync(sheetId, fields, cancellationToken);

    public Task PushTestConditionsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Editor.PushTestConditionsAsync(sheetId, cancellationToken);

    public IReadOnlyDictionary<string, string> ResolveDependentDefaults(
        string changedKey,
        string value
    ) => Editor.ResolveDependentDefaults(changedKey, value);

    public Task<bool> HasTemplateAsync(CancellationToken cancellationToken = default) =>
        Editor.HasTemplateAsync(cancellationToken);

    public Task<IReadOnlyDictionary<string, string>> ReadTemplateAsync(
        CancellationToken cancellationToken = default
    ) => Editor.ReadTemplateAsync(cancellationToken);

    public Task SaveTemplateAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => Editor.SaveTemplateAsync(fields, cancellationToken);
}

/// <summary>浏览 + 报表导出两个契约的"当前方法"代理（同 <see cref="CurrentMethodExecution"/> 的说明）。</summary>
/// <summary>浏览 + 报表导出两个契约的"当前方法"代理（构造期不碰上下文，理由同 <see cref="CurrentMethodExecution"/>）。</summary>
public sealed class CurrentMethodBrowse(Func<ICurrentMethodContext> methods)
    : ITestSheetBrowser,
        IReportExportService
{
    private ITestSheetBrowser Browser => methods().Current.Browser;

    private IReportExportService Reports => methods().Current.Reports;

    public string CurveXAxisKey => Browser.CurveXAxisKey;

    public string CurveYAxisKey => Browser.CurveYAxisKey;

    public Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
        string? keyword = null,
        CancellationToken cancellationToken = default
    ) => Browser.QueryAsync(keyword, cancellationToken);

    public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadSamplesAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Browser.ReadSamplesAsync(sheetId, cancellationToken);

    public Task<IReadOnlyList<CurvePoint>> ReadCurveAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Browser.ReadCurveAsync(sheetId, sampleId, cancellationToken);

    public Task<CurveAnnotation?> ReadAnnotationAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Browser.ReadAnnotationAsync(sheetId, sampleId, cancellationToken);

    public Task<string> ExportAsync(
        string sheetId,
        string culture,
        CancellationToken cancellationToken = default
    ) => Reports.ExportAsync(sheetId, culture, cancellationToken);

    public Task<string?> SaveCurveImageAsync(
        string sheetId,
        string sampleId,
        string culture,
        CancellationToken cancellationToken = default
    ) => Reports.SaveCurveImageAsync(sheetId, sampleId, culture, cancellationToken);
}

/// <summary>
/// 方法参数的"当前方法"代理（构造期不碰上下文，理由同 <see cref="CurrentMethodExecution"/>）。
/// **这个尤其关键**：方法工程的浏览件（方法工程里的浏览件）都要
/// <see cref="ITestParameters"/>，它就在"上下文 → 目录"这条构造链上。
/// </summary>
public sealed class CurrentMethodParameters(Func<ICurrentMethodContext> methods) : ITestParameters
{
    public IReadOnlyList<ParameterDefinition> Definitions => methods().Current.Parameters.Definitions;

    public Task<IReadOnlyDictionary<string, string>> LoadAsync(
        CancellationToken cancellationToken = default
    ) => methods().Current.Parameters.LoadAsync(cancellationToken);

    public Task SaveAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default
    ) => methods().Current.Parameters.SaveAsync(values, cancellationToken);
}
