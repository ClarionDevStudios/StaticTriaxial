namespace ANX.Method.Marshall;

using System.Globalization;
using ANX.Control;
using ANX.Storage;

/// <summary>
/// T 0709 的**方法级参数**：加载速率 / 接触判定阈值 / 破坏判据 / 破坏确认帧数 —— 与弯曲同一组键、
/// 同一套读写口径（值按方法代号分库存：<c>ParameterStore.Load("T0709")</c>）。
/// 通用参数（通道设置 / 归位时间 / 原点修正区间 / 闸门系数…）声明在 <see cref="MachineParameterCatalog"/>，
/// 界面把"通用目录 ∪ 方法参数"合并渲染，同键以方法声明为准。
///
/// 默认值的依据：规范 JTG 3410-2025 T 0709 规定加载速率 50mm/min±5；
/// 现役真库 `TestParameterConfig` 马歇尔那一行（AsphaltTestTypeID = 1、ConfigMode = 1）现场配的是
/// MachineSpeed 5 / ZeroDisplacement 10N / PressureDrop 1000N / NumberOfFilters 5 —— 现场值写进说明，
/// 默认值给规范值（与弯曲同一条处理）。
/// </summary>
public sealed class MarshallParameters(ParameterStore store) : ITestParameters
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
    /// 马歇尔自己声明的参数键（与弯曲同名同义 —— 两套方法各自存值，互不影响）。
    /// 参数配置页的排序沿用 <see cref="MachineParameterCatalog.RuntimeOrderKeys"/>，所以这里不再另排。
    /// </summary>
    public static readonly string[] MachineKeys =
    [
        LoadRateKey,
        ContactLoadKey,
        BreakDropKey,
        BreakConfirmKey,
    ];

    public string MethodCode => "T0709";

    /// <summary>
    /// 方法级参数声明。中文写在声明里当兜底，资源键按 <see cref="ParameterTextKeys"/> 的约定自动补
    /// （见 <see cref="ParameterDefinition.WithTextKeys"/>）—— 语言切换时这几项跟着走。
    /// </summary>
    public IReadOnlyList<ParameterDefinition> Definitions { get; } =
        [.. RawDefinitions().Select(definition => definition.WithTextKeys())];

    /// <summary>中文兜底的原始声明（键由 <see cref="ParameterDefinition.WithTextKeys"/> 按约定补）。</summary>
    private static IReadOnlyList<ParameterDefinition> RawDefinitions() =>
    [
        // 说明用**本方法专属**的资源键（Field.T0709.<键>.Desc）：显示名与弯曲共用（同一个词），
        // 但说明里的规范号（T0709 vs T0715）与现场配值不同，共用一个键就会给马歇尔页显示弯曲的说明
        //（声明里挂键的约定见 ParameterTextKeys / WithTextKeys）。
        new(LoadRateKey, "加载速率", "mm/min", "50",
            Kind: ParameterKind.Number, Minimum: 0.1, Maximum: 50, Scope: ParameterScope.Runtime,
            DescriptionKey: "Field.T0709.LoadRate.Desc",
            Description: "试验过程的加载速率。规范 T0709 规定 50mm/min±5；电机上限 50mm/min。现役马歇尔现场配的是 5（属现场配置）。"),
        new(ContactLoadKey, "接触判定阈值", "kN", "0.05",
            Kind: ParameterKind.Number, Minimum: 0.001, Maximum: 50, Scope: ParameterScope.Runtime,
            DescriptionKey: "Field.T0709.ContactLoad.Desc",
            Description: "荷载达到该值视为压头已接触试件（现役 ZeroDisplacement「压力小于该值时位移清零」，单位 N→kN）；接触成立时位移调零并切到试验速率。现役马歇尔配 10N（0.01kN），弯曲配 100N（0.1kN），现场按需调。"),
        new(BreakDropKey, "破坏判据（荷载下降量）", "kN", "1.0",
            Kind: ParameterKind.Number, Minimum: 0, Maximum: 50, Scope: ParameterScope.Runtime,
            DescriptionKey: "Field.T0709.BreakDrop.Desc",
            Description: "荷载自峰值下降超过该值即视为破坏（现役 PressureDrop「压力落差」，单位 N→kN）。现役马歇尔现场配 1000N = 1.0kN（弯曲现场配 500N = 0.5kN）。"),
        new(BreakConfirmKey, "破坏确认帧数", "帧", "5",
            Kind: ParameterKind.Number, Minimum: 1, Maximum: 100, Scope: ParameterScope.Runtime,
            DescriptionKey: "Field.T0709.BreakConfirmSamples.Desc",
            Description: "破坏确认所需的连续帧数（现役 NumberOfFilters，注释写「滤波次数」，实际当确认帧数用）。现役马歇尔现场配 5。"),
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
        var definition =
            Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal))
            ?? MachineParameterCatalog.Definitions.FirstOrDefault(d =>
                string.Equals(d.Key, key, StringComparison.Ordinal)
            )
            ?? throw new ArgumentException($"方法 T0709 与通用目录都没有声明参数 '{key}'", nameof(key));

        return values.TryGetValue(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : double.Parse(definition.DefaultValue, CultureInfo.InvariantCulture);
    }
}
