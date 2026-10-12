namespace ANX.Method.Bending;

using System.Globalization;
using ANX.Control;
using ANX.Storage;

/// <summary>
/// T 0715 的参数：**试件尺寸**（试样编辑页用）与**机器 / 控制参数**（参数配置页用）。
/// 试件尺寸单独取出来放常量，是因为判定算法要用它们。
/// </summary>
public sealed class BendingParameters(ParameterStore store) : ITestParameters
{
    /// <summary>加载速率（mm/min）。</summary>
    public const string LoadRateKey = "LoadRate";

    /// <summary>接触判定阈值（kN）。</summary>
    public const string ContactLoadKey = "ContactLoad";

    /// <summary>破坏判据：荷载下降量（kN）。</summary>
    public const string BreakDropKey = "BreakDrop";

    /// <summary>破坏确认连续帧数。</summary>
    public const string BreakConfirmKey = "BreakConfirmSamples";

    /// <summary>
    /// 位移闸门安全系数（×设备量程）。默认 0.95 —— 顶过位移传感器量程会压坏传感器
    ///（用户 2026-10-11 明确；现场量程 25.000mm），2026-10-12 要求做成现场可调。
    /// </summary>
    public const string DisplacementSafetyFactorKey = "DisplacementSafetyFactor";

    /// <summary>
    /// 荷载闸门安全系数（× 设备压力量程）。默认 0.95 —— 真机 100.000kN 量程 → 95kN 停机；
    /// 设备不上报量程时回落现役常量 50kN（用户 2026-10-12 定的口径）。
    /// </summary>
    public const string LoadSafetyFactorKey = "LoadSafetyFactor";

    /// <summary>
    /// 是否启用**曲线原点修正**（用户 2026-10-12：有的试验需要、有的不需要）。
    /// `1` 启用（T0715 默认）、`0` 关闭 —— 关闭时修正原点记 0、校正挠度 = 峰值挠度、应变按未修正挠度算。
    /// </summary>
    public const string EnableOriginCorrectionKey = "EnableOriginCorrection";

    /// <summary>
    /// **平台升降速率**（mm/min）—— 执行页"工作台上升 / 下降"按钮用（用户 2026-10-12：
    /// 加载速率要两个，一个控制平台升降、一个用于试验过程）。与 <see cref="LoadRateKey"/> 各自独立；
    /// 受电机上限 50mm/min 约束（换算入口会截断）。
    /// </summary>
    public const string JogSpeedKey = "JogSpeed";

    /// <summary>破坏（或量程停机）后自动复位秒数（现役 AutoFallTime「试验结束自动下降时间」）。</summary>
    public const string AutoFallTimeKey = "AutoFallTime";

    /// <summary>原点修正拟合区间起点（占峰值荷载百分数；现役 OriginCorrectionStart）。</summary>
    public const string OriginCorrectionStartKey = "OriginCorrectionStart";

    /// <summary>原点修正拟合区间终点（占峰值荷载百分数；现役 OriginCorrectionEnd）。</summary>
    public const string OriginCorrectionEndKey = "OriginCorrectionEnd";

    /// <summary>轴向压力通道号（1~4；现役 AxialPressureChannel，默认通道 1 = 100kN 主传感器）。</summary>
    public const string PressChannelKey = "PressChannel";

    /// <summary>轴向位移通道号（1~4；现役 AxialDisplacementChannel）。</summary>
    public const string DisplaceChannelKey = "DisplaceChannel";

    /// <summary>水平位移通道号（0 = 不用；现役 HorizontalDisplacementChannel）。**待接线**。</summary>
    public const string HorizontalDisplaceChannelKey = "HorizontalDisplaceChannel";

    /// <summary>启用双位移传感器（两路取平均；现役 EnableDualSensors）。**待接线**。</summary>
    public const string EnableDualSensorsKey = "EnableDualSensors";

    /// <summary>峰值荷载小于该值时不做破坏判断（现役弯曲线程常量 BREAK_MIN_LOAD_KN）。</summary>
    public const string MinBreakLoadKey = "MinBreakLoad";

    /// <summary>破坏后继续加载时间**秒**（现役 ContinueLoadingTime；这一段的数据记入曲线）。</summary>
    public const string ContinueLoadingTimeKey = "ContinueLoadingTime";

    /// <summary>锁定压力峰值对应的位移值（现役 LockDisplacement）。**待接线**。</summary>
    public const string LockDisplacementKey = "LockDisplacement";

    /// <summary>允许施力方向反转（现役 EnableReverseDirectionOfForce）。**待接线**。</summary>
    public const string ReverseForceKey = "ReverseForce";

    /// <summary>破坏点判断完全由上位机控制（现役 EnableBreakagePointJudgmentByHost）。**待接线**。</summary>
    public const string HostJudgesBreakKey = "HostJudgesBreak";

    /// <summary>数据记录方式（现役 DataRecordingMethod）。**待接线**。</summary>
    public const string DataRecordingMethodKey = "DataRecordingMethod";

    /// <summary>数据记录起点（现役 StartingPointOfDataRecording）。**待接线**。</summary>
    public const string DataRecordingStartKey = "DataRecordingStart";

    /// <summary>数据记录间隔（现役 DataRecordingInterval）。**待接线**。</summary>
    public const string DataRecordingIntervalKey = "DataRecordingInterval";

    /// <summary>机器与控制参数（参数配置页「试验参数」页显示这些）。</summary>
    public static readonly string[] MachineKeys =
    [
        LoadRateKey,
        ContactLoadKey,
        BreakDropKey,
        BreakConfirmKey,
        DisplacementSafetyFactorKey,
        LoadSafetyFactorKey,
        EnableOriginCorrectionKey,
        OriginCorrectionStartKey,
        OriginCorrectionEndKey,
        JogSpeedKey,
        AutoFallTimeKey,
    ];

    /// <summary>「通道设置」页的参数（现役 Page1：通道 + 双传感器）。</summary>
    public static readonly string[] ChannelKeys =
    [
        PressChannelKey,
        DisplaceChannelKey,
        HorizontalDisplaceChannelKey,
        EnableDualSensorsKey,
    ];

    public string MethodCode => "T0715";

    /// <summary>
    /// T 0715 **自己**的参数（方法级）。通用参数（通道设置 / 归位时间 / 原点修正区间 / 闸门系数 /
    /// 记录显示…）属于通用界面，声明在 <see cref="MachineParameterCatalog"/>；
    /// 界面把"通用目录 ∪ 方法参数"合并渲染，同键以方法声明为准。
    /// </summary>
    public IReadOnlyList<ParameterDefinition> Definitions { get; } =
    [
        new(LoadRateKey, "加载速率", "mm/min", "50",
            Kind: ParameterKind.Number, Minimum: 0.1, Maximum: 50, Scope: ParameterScope.Runtime,
            Description: "试验过程的加载速率。规范 T0715 规定 50mm/min±5；电机上限 50mm/min。现役现场配的是 5（属现场配置）。"),
        new(ContactLoadKey, "接触判定阈值", "kN", "0.05",
            Kind: ParameterKind.Number, Minimum: 0.001, Maximum: 50, Scope: ParameterScope.Runtime,
            Description: "荷载达到该值视为压头已接触试件（现役 ZeroDisplacement「压力小于该值时位移清零」，单位 N→kN）；接触成立时位移调零并切到试验速率。"),
        new(BreakDropKey, "破坏判据（荷载下降量）", "kN", "0.3",
            Kind: ParameterKind.Number, Minimum: 0, Maximum: 50, Scope: ParameterScope.Runtime,
            Description: "荷载自峰值下降超过该值即视为破坏（现役 PressureDrop「压力落差」，单位 N→kN；现场配 0.5kN）。"),
        new(BreakConfirmKey, "破坏确认帧数", "帧", "3",
            Kind: ParameterKind.Number, Minimum: 1, Maximum: 100, Scope: ParameterScope.Runtime,
            Description: "破坏确认所需的连续帧数（现役 NumberOfFilters，注释写「滤波次数」，实际当确认帧数用；现场配 5）。"),
    ];

    public Task<IReadOnlyDictionary<string, string>> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        var stored = store.Load(MethodCode);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        // 通用目录 + 方法自己的声明（同键以方法为准）—— 通用键也要读出来，否则改了不生效
        foreach (
            var definition in MachineParameterCatalog
                .Definitions.Concat(Definitions)
                .GroupBy(d => d.Key, StringComparer.Ordinal)
                .Select(group => group.Last())
        )
        {
            values[definition.Key] =
                stored.TryGetValue(definition.Key, out var value) && !string.IsNullOrWhiteSpace(value)
                    ? value
                    : definition.DefaultValue;
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(values);
    }

    public Task SaveAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default
    )
    {
        store.Save(MethodCode, values);
        return Task.CompletedTask;
    }

    /// <summary>读一个数值参数（解析失败回落到默认值）。</summary>
    public double GetDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        // 默认值可能来自方法自己的声明，也可能来自**通用参数目录**（通道/回缩/拟合区间/闸门系数…）
        var definition =
            Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal))
            ?? MachineParameterCatalog.Definitions.FirstOrDefault(d =>
                string.Equals(d.Key, key, StringComparison.Ordinal)
            )
            ?? throw new ArgumentException($"方法 T0715 与通用目录都没有声明参数 '{key}'", nameof(key));

        return values.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.Parse(definition.DefaultValue, CultureInfo.InvariantCulture);
    }
}
