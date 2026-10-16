namespace STS.Method.ShearStrength;

using System.Globalization;
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

    /// <summary>默认应变速率（%/min）：规范 4.5 的 1~2 取中值。</summary>
    public const double DefaultStrainRatePercentPerMinute = 1.5;

    /// <summary>接触应力（kPa）：规范 4.3 的 3~5kPa 取上限。</summary>
    public const double DefaultContactStressKPa = 5.0;

    public Task<TestRunProfile> LoadProfileAsync(
        TestStartContext? context = null,
        CancellationToken cancellationToken = default
    ) => LoadProfileCoreAsync(context, cancellationToken);

    /// <summary>
    /// 装配一次运行：T 1236 口径的默认值 + （有起点上下文时）按**逐件**试件尺寸
    /// 构造停止判据（15% 应变上限、校正面积应力都依赖 h0 / d0）与加载速率（应变速率 × h0）。
    /// </summary>
    private async Task<TestRunProfile> LoadProfileCoreAsync(
        TestStartContext? context,
        CancellationToken cancellationToken
    )
    {
        var nominalArea = ShearStrengthAnalyzer.InitialAreaMM2(ShearStrengthAnalyzer.NominalSpecimenDiameterMM);

        var profile = new TestRunProfile
        {
            // 规范 4.7 的卸压 / 拆室是人工环节：本方法不配"破坏后继续加载"，也不自动归位
            ContinueLoadingSeconds = 0,
            ResetSeconds = 0,
            // 接触 = 3~5kPa（取上限）；按标准件 Φ100 试件面积折算成力（≈0.0393kN）
            ContactLoadKN = DefaultContactStressKPa * nominalArea / 1_000_000.0,
            // 规范 4.5：破坏控制在 5~10min 内完成 → 加载阶段兜底超时给足余量
            LoadingTimeoutSeconds = 30 * 60,
        };

        if (context is null)
        {
            return profile;
        }

        var info = await LoadSampleInfoAsync(context.SheetId, context.SampleId, cancellationToken)
            .ConfigureAwait(false);

        if (TryReadSpecimenSize(info, out var heightMM, out var diameterMM))
        {
            return profile with
            {
                StopCriteriaFactory = () => new ShearStrengthStopCriteria(heightMM, diameterMM),
                // 加载速率 = 应变速率 × 初始高度（例：306mm × 1.5%/min ≈ 4.59mm/min）
                LoadRateMillimetersPerMinute =
                    DefaultStrainRatePercentPerMinute * heightMM / 100.0,
            };
        }

        // 骨架期：试样信息读取还是桩（回空），先不挂判据（当前只有模拟链路，不影响）；
        // 真实读取落地后，这里改成"缺尺寸 → 明确报错，提示先填试样信息"
        //（见 docs\对照\T1236-数据表设计.md 待办①）。
        return profile;
    }

    /// <summary>
    /// 从试样信息字典里读试件尺寸（键 = 明细列名 `InitialHeight` / `InitialDiameter`，单位 mm）。
    /// 两个键都在、能解析且为正才算有效。
    /// </summary>
    public static bool TryReadSpecimenSize(
        IReadOnlyDictionary<string, string> sampleInfo,
        out double heightMM,
        out double diameterMM
    )
    {
        heightMM = 0;
        diameterMM = 0;

        return sampleInfo.TryGetValue("InitialHeight", out var heightText)
            && sampleInfo.TryGetValue("InitialDiameter", out var diameterText)
            && double.TryParse(heightText, NumberStyles.Float, CultureInfo.InvariantCulture, out heightMM)
            && double.TryParse(diameterText, NumberStyles.Float, CultureInfo.InvariantCulture, out diameterMM)
            && heightMM > 0
            && diameterMM > 0;
    }

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
