namespace STS.Method.ShearStrength;

using STS.Control;
using STS.Core;

/// <summary>
/// 抗剪强度（静三轴法）的执行 / 试验单编辑实现 —— **骨架占位**：
/// 保证外壳可启动、方法目录可装载、页面拿到"有声明、实现待接线"；
/// 周内按现役流程逐步落地（联机 → 采集 / 控制 → 判定 → 落库 → 报表）。
///
/// 骨架期的口径：只读操作回空值 / 安全默认；**写操作抛 <see cref="NotSupportedException"/>**，
/// 界面上点到"还没实现"的按钮会得到一句明确说明，不会静默假动作。
/// </summary>
public sealed class ShearStrengthExecution : ITestExecution, ITestSheetEditor
{
    /// <summary>方法显示名（中文兜底；界面按 <c>MethodNameKey</c> 取当前语言）。</summary>
    public const string MethodDisplayName = "抗剪强度试验（静三轴法）";

    public string MethodCode => ShearStrengthPersistenceModule.MethodCodeValue;

    public string MethodName => MethodDisplayName;

    public string MethodNameKey => "Method.ShearStrength.Name";

    public string StandardReference => "GB/T 50123-2019 / DL/T 5356-2024（三轴压缩试验；骨架占位）";

    public string CurveXAxisKey => "Chart.Axis.Deformation";

    public string CurveYAxisKey => "Chart.Axis.Load";

    public TestRunProfile Profile { get; } = new();

    public Task<TestRunProfile> LoadProfileAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Profile);

    public IReadOnlyList<MetricDefinition> Metrics => [];

    public IReadOnlyList<ParameterDefinition> SampleFields => [];

    public Task<IReadOnlyDictionary<string, string>> LoadSampleInfoAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

    public Task<IReadOnlyList<SampleRowInfo>> ReadSampleRowsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyList<SampleRowInfo>>([]);

    public Task SaveSampleInfoAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;

    public Task<string> CreateSheetAsync(
        string projectName,
        string numberPrefix,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public Task<string> NextSampleIdAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public Task<bool> DeleteSampleAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, double>?> CompleteSampleAsync(
        string sheetId,
        string sampleId,
        IReadOnlyList<CurvePoint> curve,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IReadOnlyDictionary<string, double>?>(null);

    public Task CompleteSheetAsync(string sheetId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<ValidityReport> EvaluateValidityAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new ValidityReport(0, 0, [], false));

    public Task<RecomputeReport> RecomputeSheetAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(new RecomputeReport(0, 0));

    // ── ITestSheetEditor（骨架：声明为空、写操作抛说明）────────────────────

    public IReadOnlyList<ParameterDefinition> SheetInfoFields => [];

    public Task<string> PeekNextSheetIdAsync(CancellationToken cancellationToken = default) =>
        throw Skeleton();

    public Task<IReadOnlyDictionary<string, string>> ReadSheetInfoAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public Task<string> CreateSheetAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public Task UpdateSheetAsync(
        string sheetId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public Task PushTestConditionsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public IReadOnlyDictionary<string, string> ResolveDependentDefaults(
        string changedKey,
        string value
    ) => new Dictionary<string, string>(StringComparer.Ordinal);

    public Task<bool> HasTemplateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyDictionary<string, string>> ReadTemplateAsync(
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    public Task SaveTemplateAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    ) => throw Skeleton();

    private static NotSupportedException Skeleton() =>
        new("静三轴方法还在骨架阶段：该功能周内按现役流程实现（详见 docs / 项目卡片）。");
}
