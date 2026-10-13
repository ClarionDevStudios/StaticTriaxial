namespace ANX.Control;

using ANX.Core;

/// <summary>实时指标卡的定义（界面按它生成卡片，方法只声明"我有哪些指标"）。</summary>
/// <param name="Key">指标键，与 <see cref="ITestExecution"/> 返回的实时值字典对应。</param>
/// <param name="DisplayNameKey">显示名**资源键**（多语言），不是显示文本本身 —— 与 IAP 的字段声明同款。</param>
/// <param name="Unit">单位符号（无量纲留空）。</param>
/// <param name="ShowPeak">是否同时显示峰值。</param>
/// <param name="ValueMinChars">
/// 数值区至少预留多少个字符的宽度。给"会一直跳动、位数会涨"的实时读数用
/// （荷载、位移），免得卡片宽度随数字位数忽宽忽窄。0 = 不预留。
/// </param>
public sealed record MetricDefinition(
    string Key,
    string DisplayNameKey,
    string Unit,
    bool ShowPeak = false,
    int ValueMinChars = 0
);

/// <summary>
/// 试验执行页"试样信息列表"的一行：试件号 + 各试样信息字段 + 是否已做完试验。
/// </summary>
/// <param name="SampleId">试件号（形如 S001）。</param>
/// <param name="IsCompleted">
/// 这件试样是否**已完成试验**（明细行 <c>TestStatus</c> = 完成）。界面据此决定能不能再改它的信息
/// （用户 2026-10-13：选中的试样已完成 → 新建/编辑试样按钮置灰）。
/// </param>
/// <param name="Fields">各试样信息字段的值（键 = <see cref="ITestExecution.SampleFields"/> 的 Key）。</param>
public sealed record SampleRowInfo(
    string SampleId,
    bool IsCompleted,
    IReadOnlyDictionary<string, string> Fields
);

/// <summary>
/// 试验执行契约：界面靠它跑一轮试验，**不认识任何具体方法的算法与字段**
/// （T0715 的判定公式、库表、报表装配都留在方法工程里）。
/// </summary>
public interface ITestExecution : ICurveAxisSource
{
    /// <summary>方法代号。</summary>
    string MethodCode { get; }

    /// <summary>方法显示名。</summary>
    string MethodName { get; }

    /// <summary>依据的规范条款。</summary>
    string StandardReference { get; }

    /// <summary>运行参数与保护阈值（交给阶段机）。</summary>
    TestRunProfile Profile { get; }

    /// <summary>
    /// 按**当前存下来的方法参数**装配一次运行的参数与保护阈值
    /// （接触阈值 / 破坏判据 / 确认帧数 / 加载速率都从参数表来，不再用写死的默认值）。
    /// </summary>
    Task<TestRunProfile> LoadProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>实时指标卡。</summary>
    IReadOnlyList<MetricDefinition> Metrics { get; }

    /// <summary>
    /// **试样信息字段**声明（试样编辑页用）。与 <see cref="ITestParameters"/> 的区别：
    /// 参数是**方法级**（一次配置、每次运行生效），试样信息是**逐件填录**的（随试件存进明细行，
    /// 判定时按件取用）。试件尺寸 / 跨径属后者（用户 2026-10-12 明确）。
    /// </summary>
    IReadOnlyList<ParameterDefinition> SampleFields { get; }

    /// <summary>读一件试样的信息（没有该件记录时回落到字段默认值）。</summary>
    Task<IReadOnlyDictionary<string, string>> LoadSampleInfoAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 读**这张试验单里已有的试样信息**（每个试件一行：试件号 + <see cref="SampleFields"/> 各字段 + 是否已完成），
    /// 供试验执行页的试样信息列表显示（用户 2026-10-13）。只读，不改库；按试件号顺序返回。
    /// </summary>
    Task<IReadOnlyList<SampleRowInfo>> ReadSampleRowsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>保存一件试样的信息（明细行即"试样记录"；曲线与判定结果由试验结束时的收尾写入）。</summary>
    Task SaveSampleInfoAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    );

    /// <summary>建一张新试验单，返回单号。</summary>
    Task<string> CreateSheetAsync(string projectName, string numberPrefix, CancellationToken cancellationToken = default);

    /// <summary>取该试验单下一个可用的试件号（避免在已有单上跑新试件时覆盖旧数据）。</summary>
    Task<string> NextSampleIdAsync(string sheetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除一件试样的明细行（曲线 / 判定值 / 试样信息一起删；用户 2026-10-13）。
    /// 「删除试样」按钮用它；返回是否删到了行。
    /// </summary>
    Task<bool> DeleteSampleAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    );

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

    /// <summary>
    /// **有效性计算**（现役弯曲窗体「有效性计算」按钮 → <c>CalcAndSaveBendingStats</c>）：
    /// 按 T 0715-2025 5.6 对本单**已完成**的试件做有效性判定（与平均值偏差超过允许误差者剔除），
    /// 写回明细行的 <c>IsEffective</c> 与主表的组统计列。**不改变试验单的完成状态**。
    /// </summary>
    Task<ValidityReport> EvaluateValidityAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// **按当前参数重算**某张试验单里所有已存曲线：逐件重新判定、覆盖已存结果列，并重算组统计。
    /// 用途（用户 2026-10-12 要求）：改了"计算期参数"（跨径 / 试件尺寸 / 是否原点修正…）之后，
    /// 让历史数据按新参数重算，不必重做试验。
    /// </summary>
    Task<RecomputeReport> RecomputeSheetAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 重算结果摘要：<paramref name="Recomputed"/> 件已重算、<paramref name="Skipped"/> 件跳过
/// （曲线点不足或单件判定不了）。
/// </summary>
public sealed record RecomputeReport(int Recomputed, int Skipped);

/// <summary>有效性计算结果摘要。</summary>
/// <param name="Evaluated">参与判定的试件数。</param>
/// <param name="EffectiveCount">有效（计入统计）件数。</param>
/// <param name="ExcludedSampleIds">被剔除的试件号（超出允许误差）。</param>
/// <param name="NeedsSupplement">有效件不足 3 个，应补充试验。</param>
public sealed record ValidityReport(
    int Evaluated,
    int EffectiveCount,
    IReadOnlyList<string> ExcludedSampleIds,
    bool NeedsSupplement
);
