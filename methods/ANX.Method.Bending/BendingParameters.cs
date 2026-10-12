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
    /// <summary>跨径 L（mm）。</summary>
    public const string SpanLengthKey = "SpanLength";

    /// <summary>试件宽度 b（mm）。</summary>
    public const string SampleWidthKey = "SampleWidth";

    /// <summary>试件高度 h（mm）。</summary>
    public const string SampleHeightKey = "SampleHeight";

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

    /// <summary>试件尺寸类参数（试样编辑页只显示这些）。</summary>
    public static readonly string[] SampleKeys = [SpanLengthKey, SampleWidthKey, SampleHeightKey];

    /// <summary>机器与控制参数（参数配置页只显示这些）。</summary>
    public static readonly string[] MachineKeys =
    [
        LoadRateKey,
        ContactLoadKey,
        BreakDropKey,
        BreakConfirmKey,
        DisplacementSafetyFactorKey,
        LoadSafetyFactorKey,
        EnableOriginCorrectionKey,
        JogSpeedKey,
    ];

    public string MethodCode => "T0715";

    public IReadOnlyList<ParameterDefinition> Definitions { get; } =
    [
        // 试件尺寸：判定（计算期）用
        new(SpanLengthKey, "跨径 L", "mm", "200", ParameterGroup.Sample,
            ParameterKind.Number, 10, 1000, ParameterScope.Computation),
        new(SampleWidthKey, "试件宽度 b", "mm", "30", ParameterGroup.Sample,
            ParameterKind.Number, 1, 500, ParameterScope.Computation),
        new(SampleHeightKey, "试件高度 h", "mm", "35", ParameterGroup.Sample,
            ParameterKind.Number, 1, 500, ParameterScope.Computation),

        // 运行期：装配进 TestRunProfile，控制循环实时用
        // 加载速率上限 = 电机硬上限 50mm/min（超了伺服宕机，用户 2026-10-11）
        new(LoadRateKey, "加载速率", "mm/min", "50",
            Kind: ParameterKind.Number, Minimum: 0.1, Maximum: 50, Scope: ParameterScope.Runtime),
        new(ContactLoadKey, "接触判定阈值", "kN", "0.05",
            Kind: ParameterKind.Number, Minimum: 0.001, Maximum: 50, Scope: ParameterScope.Runtime),
        new(BreakDropKey, "破坏判据（荷载下降量）", "kN", "0.3",
            Kind: ParameterKind.Number, Minimum: 0, Maximum: 50, Scope: ParameterScope.Runtime),
        new(BreakConfirmKey, "破坏确认帧数", "帧", "3",
            Kind: ParameterKind.Number, Minimum: 1, Maximum: 100, Scope: ParameterScope.Runtime),
        new(DisplacementSafetyFactorKey, "位移闸门安全系数（×设备量程）", "×", "0.95",
            Kind: ParameterKind.Number, Minimum: 0.1, Maximum: 1.0, Scope: ParameterScope.Runtime),
        new(LoadSafetyFactorKey, "荷载闸门安全系数（×设备量程）", "×", "0.95",
            Kind: ParameterKind.Number, Minimum: 0.1, Maximum: 1.0, Scope: ParameterScope.Runtime),
        new(JogSpeedKey, "平台升降速率（工作台按钮）", "mm/min", "50",
            Kind: ParameterKind.Number, Minimum: 0.1, Maximum: 50, Scope: ParameterScope.Runtime),

        // 计算期：判定 / 重算时用
        new(EnableOriginCorrectionKey, "启用曲线原点修正", "", "1",
            Kind: ParameterKind.Switch, Scope: ParameterScope.Computation),
    ];

    public Task<IReadOnlyDictionary<string, string>> LoadAsync(
        CancellationToken cancellationToken = default
    )
    {
        var stored = store.Load(MethodCode);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var definition in Definitions)
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
        var definition = Definitions.First(d => string.Equals(d.Key, key, StringComparison.Ordinal));

        return values.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.Parse(definition.DefaultValue, CultureInfo.InvariantCulture);
    }
}
