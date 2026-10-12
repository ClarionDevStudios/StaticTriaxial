namespace ANX.Control;

/// <summary>
/// **通用参数目录**（比照现役 uConfigTestParameterForm：那个窗体是"全部试验方法用到的参数超集"）。
///
/// 这里声明的是**与具体试验方法无关**的机器/采集/判定口径参数；参数**值**仍按方法代号分库存
/// （现役同样是"窗体通用、值按 (方法类型, 配置模式) 存"）。
/// 方法自己的参数（例如 T0715 的加载速率/接触阈值/破坏判据/确认帧数）由方法声明，
/// 界面把"通用目录 ∪ 方法参数"合并渲染，**同键以方法声明为准**。
/// </summary>
public static class MachineParameterCatalog
{
    // ── 通道设置 ────────────────────────────────────────────────────────────
    public const string PressChannelKey = "PressChannel";

    public const string DisplaceChannelKey = "DisplaceChannel";

    public const string HorizontalPressChannelKey = "HorizontalPressChannel";

    public const string HorizontalDisplaceChannelKey = "HorizontalDisplaceChannel";

    // ── 试验参数 ────────────────────────────────────────────────────────────
    public const string AutoFallTimeKey = "AutoFallTime";

    public const string EnableOriginCorrectionKey = "EnableOriginCorrection";

    public const string OriginCorrectionStartKey = "OriginCorrectionStart";

    public const string OriginCorrectionEndKey = "OriginCorrectionEnd";

    public const string DisplacementSafetyFactorKey = "DisplacementSafetyFactor";

    public const string LoadSafetyFactorKey = "LoadSafetyFactor";

    public const string JogSpeedKey = "JogSpeed";

    public const string MinBreakLoadKey = "MinBreakLoad";

    public const string ContinueLoadingTimeKey = "ContinueLoadingTime";

    public const string LockDisplacementKey = "LockDisplacement";

    public const string ReverseForceKey = "ReverseForce";

    public const string HostJudgesBreakKey = "HostJudgesBreak";

    public const string DataRecordingMethodKey = "DataRecordingMethod";

    public const string DataRecordingStartKey = "DataRecordingStart";

    public const string DataRecordingIntervalKey = "DataRecordingInterval";

    /// <summary>压力通道选项：压力通道 1~4（值 = 现役的 0 基通道索引）。</summary>
    private static readonly IReadOnlyList<ParameterOption> PressChannelOptions =
    [
        new("0", "压力通道1"),
        new("1", "压力通道2"),
        new("2", "压力通道3"),
        new("3", "压力通道4"),
    ];

    /// <summary>
    /// 位移通道选项（值 = 现役 AxialDisplacementChannel 的编号，与现役 <c>ResolveAxialDisplacementChannels</c> 一致）：
    /// 0~3 位移通道 1~4；4~7 千分表 1~4；8~11 两组双通道取平均。
    /// </summary>
    private static readonly IReadOnlyList<ParameterOption> DisplaceChannelOptions =
    [
        new("0", "位移通道1"),
        new("1", "位移通道2"),
        new("2", "位移通道3"),
        new("3", "位移通道4"),
        new("4", "千分表1"),
        new("5", "千分表2"),
        new("6", "千分表3"),
        new("7", "千分表4"),
        new("8", "位移1+2通道平均值"),
        new("9", "位移3+4通道平均值"),
        new("10", "千分表1+2通道平均值"),
        new("11", "千分表3+4通道平均值"),
    ];

    /// <summary>水平通道在正常选项前加一项"不用"。</summary>
    private static IReadOnlyList<ParameterOption> WithUnused(IReadOnlyList<ParameterOption> options) =>
        [new("12", "不用"), .. options];

    /// <summary>
    /// 试验参数页「运行期」卡片的显示顺序（用户 2026-10-12 指定；键不在此表里的排在后面）。
    /// 末两项按**生效时点**排：破坏后继续加载时间 → 试验完成后归位时间（用户 2026-10-12 追加）。
    /// </summary>
    public static readonly IReadOnlyList<string> RuntimeOrderKeys =
    [
        JogSpeedKey,
        BendingLoadRateKey,
        DisplacementSafetyFactorKey,
        LoadSafetyFactorKey,
        BendingContactLoadKey,
        MinBreakLoadKey,
        BendingBreakDropKey,
        BendingConfirmKey,
        ContinueLoadingTimeKey,
        AutoFallTimeKey,
    ];

    // 方法侧的同名键（T0715 声明自己的加载速率/接触阈值/破坏判据/确认帧数）—— 只为排序引用
    private const string BendingLoadRateKey = "LoadRate";

    private const string BendingContactLoadKey = "ContactLoad";

    private const string BendingBreakDropKey = "BreakDrop";

    private const string BendingConfirmKey = "BreakConfirmSamples";

    /// <summary>通用参数声明（界面按 Scope 分页签、按作用时点分区）。</summary>
    public static IReadOnlyList<ParameterDefinition> Definitions { get; } =
    [
        // ── 通道设置 ────────────────────────────────────────────────────────
        new(PressChannelKey, "轴向压力通道", "", "1", ParameterGroup.Machine,
            ParameterKind.Choice, Scope: ParameterScope.Acquisition, Options: PressChannelOptions,
            Description: "轴向压力取自哪个通道；**保存即时生效**，荷载读数立刻按新通道取。现役默认通道 1 = 100.000kN 主传感器（通道 2 是 5kN 小量程）。"),
        new(DisplaceChannelKey, "轴向位移通道", "", "2", ParameterGroup.Machine,
            ParameterKind.Choice, Scope: ParameterScope.Acquisition, Options: DisplaceChannelOptions,
            Description: "轴向位移取自哪个通道；**保存即时生效**，位移读数立刻按新通道取。真机两路位移量程都是 25.000mm。"),
        new(HorizontalPressChannelKey, "水平压力通道", "", "0", ParameterGroup.Machine,
            ParameterKind.Choice, Scope: ParameterScope.Acquisition, Options: WithUnused(PressChannelOptions),
            Description: "水平压力取自哪个通道（0 = 不用）。当前判定不使用该通道，先把现场配置存下来。"),
        new(HorizontalDisplaceChannelKey, "水平位移通道", "", "0", ParameterGroup.Machine,
            ParameterKind.Choice, Scope: ParameterScope.Acquisition, Options: WithUnused(DisplaceChannelOptions),
            Description: "水平位移取自哪个通道（0 = 不用）。当前判定不使用该通道，先把现场配置存下来。"),

        // ── 试验参数：运行期 ────────────────────────────────────────────────
        // 用户 2026-10-12 更名：原「破坏后回缩时间」→「试验完成后归位时间」，并移到运行期最后一行
        // （它的生效时点在「破坏后继续加载时间」之后：破坏确认 → 继续加载 → 归位）。
        new(AutoFallTimeKey, "试验完成后归位时间", "s", "5", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 0, Maximum: 600, Scope: ParameterScope.Runtime,
            Description: "试验完成（破坏确认，或量程停机）后，电机反转把工作台送回起始位置所用的秒数（现役 AutoFallTime「试验结束自动下降时间」；现场配 5s）。**归位段的数据不记入曲线**（曲线只体现到破坏为止的加载过程）；生效时点在「破坏后继续加载时间」之后：破坏确认 → 继续加载 → 归位。"),
        new(JogSpeedKey, "平台升降速率", "mm/min", "50", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 0.1, Maximum: 50, Scope: ParameterScope.Runtime,
            Description: "执行页「工作台上升 / 下降」按钮的升降速率，与试验加载速率分开；受电机 50mm/min 上限约束。"),
        new(DisplacementSafetyFactorKey, "位移闸门安全系数", "×量程", "0.95", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 0.1, Maximum: 1.0, Scope: ParameterScope.Runtime,
            Description: "位移闸门 = 设备位移量程 × 本系数（真机 25mm → 23.75mm）。超量程会压坏位移传感器。"),
        new(LoadSafetyFactorKey, "荷载闸门安全系数", "×量程", "0.95", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 0.1, Maximum: 1.0, Scope: ParameterScope.Runtime,
            Description: "荷载闸门 = 设备压力量程 × 本系数（真机 100kN → 95kN）；取不到量程时回落现役常量 50kN。"),
        // 已接线（2026-10-12）：峰值荷载小于它就不判破坏，对应现役弯曲线程常量 BREAK_MIN_LOAD_KN = 0.2
        new(MinBreakLoadKey, "不判破坏荷载下限", "kN", "0.2", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 0, Maximum: 50, Scope: ParameterScope.Runtime,
            Description: "**峰值荷载**小于该值时不判断破坏（现役弯曲线程 `MaxLoad >= BREAK_MIN_LOAD_KN`；现场配 200N = 0.2kN）。0 = 不启用下限。"),
        // 单位是**秒**（用户 2026-10-12 明确）：与现役各试验线程 `CurrentTime - FPostBreakStartTime >=
        // ContinueLoadingTime` 的口径一致（CurrentTime = Stopwatch.Elapsed.TotalSeconds）。
        // 注意现役自身的注释写的是 (ms)，属遗留误标 —— 见 docs/对照/参数表-消费时点与现役对照.md。
        new(ContinueLoadingTimeKey, "破坏后继续加载时间", "s", "5", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 0, Maximum: 60, Scope: ParameterScope.Runtime,
            Description: "破坏确认后仍继续加载的时间（单位**秒**），让曲线走出一段破坏后的荷载下降趋势（现役 ContinueLoadingTime）。**这一段的数据记入曲线**；走完再按「试验完成后归位时间」反转归位（归位段不记曲线）。0 = 不做继续加载，确认后直接归位。将来把这一项下发给控制器（C1H 第 9 字节）时要 ×10 换成 0.1s 单位。"),
        // ── 试验参数：计算期 ────────────────────────────────────────────────
        new(EnableOriginCorrectionKey, "启用曲线原点修正", "", "1", ParameterGroup.Machine,
            ParameterKind.Switch, Scope: ParameterScope.Computation,
            Description: "是否做曲线原点修正（有的试验需要、有的不需要）。关闭时修正原点记 0、校正挠度 = 峰值挠度、应变按未修正挠度算。"),
        new(OriginCorrectionStartKey, "原点修正区间起点", "%峰值", "10", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 1, Maximum: 99, Scope: ParameterScope.Computation,
            Description: "拟合修正直线的荷载区间起点（占峰值荷载百分数）。规范未规定拟合方法；现役代码默认 10，现场配 20。"),
        new(OriginCorrectionEndKey, "原点修正区间终点", "%峰值", "80", ParameterGroup.Machine,
            ParameterKind.Number, Minimum: 1, Maximum: 100, Scope: ParameterScope.Computation,
            Description: "拟合修正直线的荷载区间终点（占峰值荷载百分数）。现役代码默认 80，现场配 60。"),
        // 每个试验都要用的默认选中项（用户 2026-10-12：锁定峰值位移，每个试验都需要锁定）
        new(LockDisplacementKey, "锁定峰值位移", "", "1", ParameterGroup.Machine,
            ParameterKind.Switch, Scope: ParameterScope.Computation,
            Description: "跨中挠度取峰值荷载点的位移（现役 LockDisplacement）；每个试验默认选中。取消则取曲线最大位移。"),

    ];
}
