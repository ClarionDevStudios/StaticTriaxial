namespace ANX.Method.Marshall;

using ANX.Control;
using ANX.Core;
using ANX.Core.Control;
using ANX.Storage;

/// <summary>
/// T 0709 的执行实现：把界面要的能力（建单 / 判定一个试件 / 收尾一组）接到
/// 已有的判定器与存储上。界面只认 <see cref="ITestExecution"/>。
///
/// 判定口径照现役 `uMarshallStabilityTestForm`：
/// <list type="bullet">
///   <item>曲线末端仍是最高荷载（未出现下降沿）→ 走 6.2 不明显峰（1.5mm 偏移线交点）；
///         否则走 6.1 明显峰（取曲线最大荷载）；</item>
///   <item>「试件高度修正系数 K」只在**路面芯样马歇尔**（试验依据 = 1，T 0710-2011）时算，
///         越界按边缘两点外推（现役 <c>OOB_EXTRAPOLATE</c>）；混合料马歇尔 K 与修正稳定度记 0
///         （现役同款：那一支根本不赋值）；</item>
///   <item>稳定度/流值/马歇尔模数的修约口径由 <see cref="MarshallAnalyzer"/> 负责。</item>
/// </list>
/// </summary>
public sealed class MarshallExecution(TestSheetService sheets, MarshallParameters? parameters = null)
    : ITestExecution, ITestSheetEditor
{
    /// <summary>
    /// 单号前缀：本项目自己的约定（弯曲用 `WT`，马歇尔用 `MS`）。
    /// 现役是统一 `T + yyyyMMdd + 000 序号`（`uFunctions.GenerateTestSheetID`），这里不照抄。
    /// </summary>
    private const string NumberPrefix = "MS";

    /// <summary>明细行的试验状态：**完成**（现役 `tsCompleted = 2`）。</summary>
    private const int TestStatusCompleted = 2;

    /// <summary>试验依据代号：路面芯样马歇尔（照现役 `TTestSpecificationsID`：0 混合料 / 1 芯样）。</summary>
    private const int CoreSampleSpecificationId = 1;

    /// <summary>稳定度偏离本组均值超过这个比例就剔除（现役 `UAsphaltMarshallValidity`：0.15）。</summary>
    private const double StabilityDeviationLimit = 0.15;

    /// <summary>有效件少于这个数不写组结果（现役"有效平行试样个数少于 3 个"）。</summary>
    private const int MinimumEffectiveSamples = 3;

    public string MethodCode => "T0709";

    /// <summary>方法名（中文兜底；界面按 <see cref="MethodNameKey"/> 取当前语言）。</summary>
    public string MethodName => "沥青混合料马歇尔稳定度试验";

    /// <inheritdoc />
    public string MethodNameKey => MarshallMessageKeys.MethodName;

    public string StandardReference => "JTG 3410-2025 T 0709-2025";

    /// <summary>曲线横轴：变形（mm）。</summary>
    public string CurveXAxisKey => MarshallCurveAxes.Deformation;

    /// <summary>曲线纵轴：荷载（kN）。</summary>
    public string CurveYAxisKey => MarshallCurveAxes.Load;

    /// <summary>
    /// 本方法的运行参数兜底值（照现役马歇尔那一行配置与本方法规范；参数表没存过值就用它们）。
    /// </summary>
    public TestRunProfile Profile { get; } = new()
    {
        ContactLoadKN = 0.05,
        BreakDropKN = 1.0,
        BreakConfirmSamples = 5,
        MaxLoadKN = 50.0,
        MaxDisplacementMM = 50.0,
        ContactTimeoutSeconds = 300.0,
        LoadingTimeoutSeconds = 600.0,
        MinBreakLoadKN = 0.2,
        ContinueLoadingSeconds = 5.0,
        ResetSeconds = 5.0,
        LoadRateMillimetersPerMinute = 50.0,
        ApproachSpeedMillimetersPerMinute = 10.0,
        DisplacementRangeSafetyFactor = 0.95,
        LoadRangeSafetyFactor = 0.95,
        JogSpeedMillimetersPerMinute = 50.0,
    };

    /// <summary>按参数表装配运行参数（接触阈值 / 破坏判据 / 确认帧数 / 加载速率 / 闸门系数 / 通道…）。</summary>
    public async Task<TestRunProfile> LoadProfileAsync(CancellationToken cancellationToken = default)
    {
        if (parameters is null)
        {
            return Profile;
        }

        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);

        return Profile with
        {
            ContactLoadKN = parameters.GetDouble(values, MarshallParameters.ContactLoadKey),
            BreakDropKN = parameters.GetDouble(values, MarshallParameters.BreakDropKey),
            BreakConfirmSamples = (int)
                Math.Max(1, parameters.GetDouble(values, MarshallParameters.BreakConfirmKey)),
            LoadRateMillimetersPerMinute = parameters.GetDouble(
                values,
                MarshallParameters.LoadRateKey
            ),
            DisplacementRangeSafetyFactor = parameters.GetDouble(
                values,
                MachineParameterCatalog.DisplacementSafetyFactorKey
            ),
            LoadRangeSafetyFactor = parameters.GetDouble(
                values,
                MachineParameterCatalog.LoadSafetyFactorKey
            ),
            JogSpeedMillimetersPerMinute = parameters.GetDouble(
                values,
                MachineParameterCatalog.JogSpeedKey
            ),
            ContinueLoadingSeconds = parameters.GetDouble(
                values,
                MachineParameterCatalog.ContinueLoadingTimeKey
            ),
            MinBreakLoadKN = parameters.GetDouble(values, MachineParameterCatalog.MinBreakLoadKey),
            ResetSeconds = parameters.GetDouble(values, MachineParameterCatalog.AutoFallTimeKey),
            PressChannelNumber = (int)
                parameters.GetDouble(values, MachineParameterCatalog.PressChannelKey),
            DisplacementSource = DisplacementSources.Parse(
                (int)parameters.GetDouble(values, MachineParameterCatalog.DisplaceChannelKey)
            ),
        };
    }

    /// <summary>试样信息字段（逐件填录）：试样描述 / 分组 / 初始直径 / 初始高度 / 加载速度。</summary>
    public IReadOnlyList<ParameterDefinition> SampleFields => MarshallSampleFields.Definitions;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LoadSampleInfoAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var columns = MarshallSampleFields.Definitions.Select(d => d.Key).ToArray();
        var row = sheets.ReadDetailRow(sheetId, sampleId, columns);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in MarshallSampleFields.Definitions)
        {
            var stored =
                row is not null
                && row.TryGetValue(definition.Key, out var value)
                && value is not null
                    ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                    : null;

            values[definition.Key] = string.IsNullOrWhiteSpace(stored) ? definition.DefaultValue : stored;
        }

        return await Task.FromResult<IReadOnlyDictionary<string, string>>(values).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> DeleteSampleAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    ) => sheets.DeleteSampleAsync(sheetId, sampleId, cancellationToken);

    // ── 试验单编辑（ITestSheetEditor）──────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<ParameterDefinition> SheetInfoFields => MarshallSheetFields.Definitions;

    /// <inheritdoc />
    public Task<string> PeekNextSheetIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(sheets.PeekNextSheetId(MethodCode, NumberPrefix));

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadSheetInfoAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = MarshallSheetFields.Definitions.Select(definition => definition.Key).ToArray();
        return Task.FromResult(ToFieldTexts(sheets.ReadMainRow(sheetId, keys)));
    }

    /// <inheritdoc />
    public async Task<string> CreateSheetAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        ValidateSheetFields(fields);

        // 单号由流水生成器发（界面上显示的只是"预览号"）—— 把界面带上来的单号剔掉
        var payload = fields
            .Where(pair => !string.Equals(pair.Key, "SheetID", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var sheetId = await sheets
            .CreateAsync(MethodCode, NumberPrefix, BuildMainColumns(payload), cancellationToken)
            .ConfigureAwait(false);

        // 马歇尔的加载速度在**试样信息**里逐件记录（照现役），试验单上没有这一项 ——
        // 所以本方法没有"把单上的条件写回参数表"这一步（与弯曲的 PushTestConditions 不对称是有意的）
        _ = sheetId;
        return sheetId;
    }

    /// <summary>
    /// 必填校验（方法层的事 —— 界面不认识"哪个字段必须填"）。
    /// 清单照现役 `TEditRoadMarshallStabilityTestForm.ValidatingInput`：项目名称、试验依据、
    /// 试验方法、试件尺寸、沥青混合料类型、试验温度；体积指标 6 项照本项目的口径**允许留空**
    /// （比现役松：现役连体积指标也要求能转数字，空串会报"格式错误"—— 见 docs 对照）。
    /// </summary>
    private static readonly HashSet<string> RequiredSheetFields = new(StringComparer.Ordinal)
    {
        MarshallSheetFields.ProjectNameKey,
        MarshallSheetFields.SpecificationKey,
        MarshallSheetFields.StandardsKey,
        MarshallSheetFields.SampleSizeKey,
        MarshallSheetFields.MixtureTypeKey,
        MarshallSheetFields.TemperatureKey,
    };

    private static bool IsRequired(string key) => RequiredSheetFields.Contains(key);

    private static void ValidateSheetFields(IReadOnlyDictionary<string, string> fields)
    {
        foreach (var definition in MarshallSheetFields.Definitions)
        {
            if (
                IsRequired(definition.Key)
                && string.IsNullOrWhiteSpace(fields.GetValueOrDefault(definition.Key))
            )
            {
                var key = definition.Kind == ParameterKind.Choice
                    ? MarshallMessageKeys.FieldChoose
                    : MarshallMessageKeys.FieldRequired;

                throw LocalizedException.Single(
                    key,
                    $"{definition.DisplayName.TrimEnd(' ', '*')}不能为空",
                    MarshallMessageKeys.FieldName(definition.DisplayNameKey)
                );
            }
        }

        foreach (var definition in MarshallSheetFields.Definitions)
        {
            var text = fields.GetValueOrDefault(definition.Key)?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            switch (definition.Kind)
            {
                case ParameterKind.Number:
                    if (
                        !double.TryParse(
                            text,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var number
                        )
                    )
                    {
                        throw LocalizedException.Single(
                            MarshallMessageKeys.FieldFormat,
                            $"{definition.DisplayName.TrimEnd(' ', '*')}格式不对",
                            MarshallMessageKeys.FieldName(definition.DisplayNameKey)
                        );
                    }

                    if (
                        (definition.Minimum is { } min && number < min)
                        || (definition.Maximum is { } max && number > max)
                    )
                    {
                        var minimum =
                            definition.Minimum?.ToString(
                                System.Globalization.CultureInfo.InvariantCulture
                            ) ?? "−∞";
                        var maximum =
                            definition.Maximum?.ToString(
                                System.Globalization.CultureInfo.InvariantCulture
                            ) ?? "+∞";

                        throw LocalizedException.Single(
                            MarshallMessageKeys.FieldOutOfRange,
                            $"{definition.DisplayName.TrimEnd(' ', '*')}必须在 {minimum}~{maximum} 之间",
                            MarshallMessageKeys.FieldName(definition.DisplayNameKey),
                            minimum,
                            maximum
                        );
                    }

                    break;

                case ParameterKind.Choice:
                    if (
                        !int.TryParse(
                            text,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var choice
                        )
                        || !ChoiceIsValid(definition, choice, fields)
                    )
                    {
                        throw LocalizedException.Single(
                            MarshallMessageKeys.FieldChoose,
                            $"请选择{definition.DisplayName.TrimEnd(' ', '*')}",
                            MarshallMessageKeys.FieldName(definition.DisplayNameKey)
                        );
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// 选项校验要**跟着试验依据走**：路面芯样只有标准马歇尔与两个芯样尺寸档
    /// （现役 `cbbTestSpecificationsChange` 会重建下拉项）。
    /// </summary>
    private static bool ChoiceIsValid(
        ParameterDefinition definition,
        int choice,
        IReadOnlyDictionary<string, string> fields
    )
    {
        if (choice < 0)
        {
            return false;
        }

        var specification = ReadChoice(fields, MarshallSheetFields.SpecificationKey, 0);

        return definition.Key switch
        {
            MarshallSheetFields.StandardsKey => choice < MarshallSheetFields.StandardsFor(specification).Count,
            MarshallSheetFields.SampleSizeKey => choice < MarshallSheetFields.SampleSizesFor(specification).Count,
            _ => definition.Options is { } options && choice < options.Count,
        };
    }

    /// <inheritdoc />
    public async Task UpdateSheetAsync(
        string sheetId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        ValidateSheetFields(fields);

        // 单号与试验时间不归这一步管：单号是主键（也是 WHERE 条件），试验时间是该单的原始记录
        var payload = fields
            .Where(pair => pair.Key is not ("SheetID" or "CreatedOn"))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        await sheets
            .UpdateMainFieldsAsync(
                sheetId,
                BuildMainColumns(payload, includeBlank: true),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 把界面填的一组字段翻译成主表列：数值列按 double、选项列按代号，
    /// 并补齐现役那种 `XxxID + XxxDesc` 成对写法（`TestSpecificationsDesc` / `TestStandardsDesc` /
    /// `SampleSizeDesc`）。**描述文字跟着试验依据走**（芯样档的文字与混合料档不同）。
    /// </summary>
    private static Dictionary<string, object?> BuildMainColumns(
        IReadOnlyDictionary<string, string> fields,
        bool includeBlank = false
    )
    {
        var columns = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var definition in MarshallSheetFields.Definitions)
        {
            if (!fields.TryGetValue(definition.Key, out var text))
            {
                continue;
            }

            text = text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                if (includeBlank)
                {
                    columns[definition.Key] = null;

                    if (DescriptionColumn(definition.Key) is { } blankDesc)
                    {
                        columns[blankDesc] = null;
                    }
                }

                continue;
            }

            switch (definition.Kind)
            {
                case ParameterKind.Number:
                    if (
                        double.TryParse(
                            text,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var number
                        )
                    )
                    {
                        columns[definition.Key] = number;
                    }

                    break;

                case ParameterKind.Choice:
                    if (
                        int.TryParse(
                            text,
                            System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var choice
                        )
                    )
                    {
                        columns[definition.Key] = choice;

                        if (
                            DescriptionText(definition.Key, choice, fields) is { Length: > 0 } label
                            && DescriptionColumn(definition.Key) is { } desc
                        )
                        {
                            columns[desc] = label;
                        }
                    }

                    break;

                default:
                    columns[definition.Key] = text;
                    break;
            }
        }

        return columns;
    }

    /// <summary>选项列对应的"描述列"（现役 `XxxID + XxxDesc` 成对）。</summary>
    private static string? DescriptionColumn(string key) =>
        key switch
        {
            MarshallSheetFields.SpecificationKey => "TestSpecificationsDesc",
            MarshallSheetFields.StandardsKey => "TestStandardsDesc",
            MarshallSheetFields.SampleSizeKey => "SampleSizeDesc",
            _ => null,
        };

    /// <summary>选项代号 → 描述文字（**文字列表按试验依据选**：芯样与混合料不同）。</summary>
    private static string? DescriptionText(
        string key,
        int choice,
        IReadOnlyDictionary<string, string> fields
    )
    {
        var specification = ReadChoice(fields, MarshallSheetFields.SpecificationKey, 0);

        IReadOnlyList<string> labels = key switch
        {
            MarshallSheetFields.SpecificationKey => MarshallSheetFields.SpecificationLabels,
            MarshallSheetFields.StandardsKey => MarshallSheetFields.StandardsFor(specification),
            MarshallSheetFields.SampleSizeKey => MarshallSheetFields.SampleSizesFor(specification),
            _ => [],
        };

        return choice >= 0 && choice < labels.Count ? labels[choice] : null;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ResolveDependentDefaults(
        string changedKey,
        string value
    )
    {
        if (!string.Equals(changedKey, MarshallSheetFields.SpecificationKey, StringComparison.Ordinal))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // 现役 cbbTestSpecificationsChange：换试验依据时试验方法与试件尺寸都重建、并选中第一项
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MarshallSheetFields.StandardsKey] = "0",
            [MarshallSheetFields.SampleSizeKey] = "0",
        };
    }

    /// <inheritdoc />
    public Task PushTestConditionsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        // 马歇尔没有"单上填的加载速率"（它逐件记在试样信息里），控制速率来自参数表 —— 所以这里是空实现。
        // 留着这个方法是因为它是一个**契约**：谁将来给这张单加了控制类条件，在这里接上即可。
        _ = sheetId;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HasTemplateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            sheets.ReadTemplateRow(MethodCode, [MarshallSheetFields.ProjectNameKey]) is not null
        );

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadTemplateAsync(
        CancellationToken cancellationToken = default
    )
    {
        var keys = MarshallSheetFields
            .Definitions.Select(definition => definition.Key)
            .Where(key => !TemplateExcludedKeys.Contains(key, StringComparer.Ordinal))
            .ToArray();

        var row = sheets.ReadTemplateRow(MethodCode, keys);

        return Task.FromResult(row is null ? DefaultFieldTexts() : ToFieldTexts(row));
    }

    /// <inheritdoc />
    public Task SaveTemplateAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        var columns = BuildMainColumns(fields);
        foreach (var key in TemplateExcludedKeys)
        {
            columns.Remove(key);
        }

        return sheets.SaveTemplateAsync(MethodCode, columns, cancellationToken);
    }

    /// <summary>模板表里没有的字段（单号自动生成、试验时间每次现取）。</summary>
    private static readonly string[] TemplateExcludedKeys = ["SheetID", "CreatedOn"];

    /// <summary>读回的主表行 → 界面字段文本（空值给空串，由界面决定显示）。</summary>
    private static IReadOnlyDictionary<string, string> ToFieldTexts(
        IReadOnlyDictionary<string, object?> row
    ) =>
        MarshallSheetFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition =>
                row.TryGetValue(definition.Key, out var value) && value is not null
                    ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                        ?? string.Empty
                    : string.Empty,
            StringComparer.Ordinal
        );

    /// <summary>各字段的默认值（没有模板时用）。</summary>
    private static IReadOnlyDictionary<string, string> DefaultFieldTexts() =>
        MarshallSheetFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition => definition.DefaultValue,
            StringComparer.Ordinal
        );

    /// <inheritdoc />
    public Task<IReadOnlyList<SampleRowInfo>> ReadSampleRowsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var columns = new[]
        {
            "SampleID",
            "SheetIndex",
            "TestStatus",
            // 「有效性计算」要看"这件有没有判定值"——按判定值判断，不只看状态列
            "MSRaw",
            "FlowValue",
        }
            .Concat(MarshallSampleFields.Definitions.Select(definition => definition.Key))
            .ToArray();

        var rows = sheets
            .ReadDetailRows(sheetId, columns)
            .OrderBy(row =>
                TryReadIndex(row["SheetIndex"])
                ?? SampleNumbering.ParseIndex(
                    Convert.ToString(
                        row["SampleID"],
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                )
                ?? int.MaxValue
            )
            .ThenBy(
                row =>
                    Convert.ToString(
                        row["SampleID"],
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                StringComparer.Ordinal
            )
            .ToArray();

        var result = new List<SampleRowInfo>(rows.Length);

        foreach (var row in rows)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var definition in MarshallSampleFields.Definitions)
            {
                fields[definition.Key] = FormatSampleField(row, definition);
            }

            result.Add(
                new SampleRowInfo(
                    Convert.ToString(
                        row["SampleID"],
                        System.Globalization.CultureInfo.InvariantCulture
                    ) ?? string.Empty,
                    TryReadIndex(row["TestStatus"]) == TestStatusCompleted,
                    TryReadDouble(row, "MSRaw", out _) && TryReadDouble(row, "FlowValue", out _),
                    fields
                )
            );
        }

        return Task.FromResult<IReadOnlyList<SampleRowInfo>>(result);
    }

    /// <summary>明细行上的一个试样信息字段取成显示文本（数值按 0.### 显示；空值回落到字段默认值）。</summary>
    private static string FormatSampleField(
        IReadOnlyDictionary<string, object?> row,
        ParameterDefinition definition
    )
    {
        if (!row.TryGetValue(definition.Key, out var raw) || raw is null || raw is DBNull)
        {
            return definition.DefaultValue;
        }

        return raw is double number
            ? number.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            : Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)
                ?? string.Empty;
    }

    /// <inheritdoc />
    public Task SaveSampleInfoAsync(
        string sheetId,
        string sampleId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        // 按字段类型分别落库（框架已支持"任意值"）：数值 → double、选项 → int、文本 → 字符串
        // （空文本存 NULL）。"试样描述"就是文本字段那种（用户 2026-10-16 要求加回来）。
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var definition in MarshallSampleFields.Definitions)
        {
            var text = fields.GetValueOrDefault(definition.Key)?.Trim() ?? string.Empty;

            values[definition.Key] = definition.Kind switch
            {
                ParameterKind.Number => MarshallSampleFields.GetDouble(fields, definition.Key),
                ParameterKind.Choice
                    when int.TryParse(
                        text,
                        System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var choice
                    ) => choice,
                ParameterKind.Choice => MarshallSampleFields.GetDouble(fields, definition.Key),
                _ => text.Length == 0 ? null : text,
            };
        }

        return sheets.UpsertSampleInfoAsync(sheetId, sampleId, values, cancellationToken);
    }

    /// <summary>实时指标卡：与现役马歇尔执行页的读数卡同构（荷载 / 变形 / 稳定度 / 流值 / 修正原点 / 模数 / 时间）。</summary>
    /// <remarks>
    /// 稳定度 / 流值 / 修正原点 / 马歇尔模数四张卡的数值格式写死 <c>0.00</c>：
    /// 这四个量按用户 2026-10-16 定的口径是**两位小数**（取值 / 存储 / 显示一致），
    /// 显示不能停留在"最多三位、去尾零"的默认格式上（否则 9.50 会显示成 9.5）。
    /// 荷载与变形两张实时读数卡也按 <c>0.00</c> —— 现役马歇尔页面的读数是
    /// <c>Format('%.2f', …)</c>（两位），口径一致。
    /// </remarks>
    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new("load", MarshallMetricKeys.Load, "kN", ShowPeak: true, ValueMinChars: 8, ValueFormat: "0.00"),
        new("displacement", MarshallMetricKeys.Displacement, "mm", ShowPeak: true, ValueMinChars: 8, ValueFormat: "0.00"),
        new("stability", MarshallMetricKeys.Stability, "kN", ValueFormat: "0.00"),
        new("flowValue", MarshallMetricKeys.FlowValue, "mm", ValueFormat: "0.00"),
        new("originCorrection", MarshallMetricKeys.OriginCorrection, "mm", ValueFormat: "0.00"),
        new("marshallModulus", MarshallMetricKeys.MarshallModulus, "kN/mm", ValueFormat: "0.00"),
        new("elapsed", MarshallMetricKeys.Elapsed, "s"),
    ];

    public Task<string> CreateSheetAsync(
        string projectName,
        string numberPrefix,
        CancellationToken cancellationToken = default
    ) => sheets.CreateAsync(MethodCode, projectName, numberPrefix, cancellationToken);

    /// <summary>下一个试件号 = 现有明细里最大的数字号 + 1（没有则 S001），口径与弯曲一致。</summary>
    public Task<string> NextSampleIdAsync(string sheetId, CancellationToken cancellationToken = default)
    {
        var rows = sheets.ReadDetailRows(sheetId, ["SampleID", "SheetIndex"]);

        var max = 0;
        foreach (var row in rows)
        {
            var index =
                TryReadIndex(row["SheetIndex"])
                ?? SampleNumbering.ParseIndex(
                    Convert.ToString(
                        row["SampleID"],
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                );

            if (index > max)
            {
                max = index.Value;
            }
        }

        return Task.FromResult(SampleNumbering.Format(max + 1));
    }

    public async Task<IReadOnlyDictionary<string, double>?> CompleteSampleAsync(
        string sheetId,
        string sampleId,
        IReadOnlyList<CurvePoint> curve,
        CancellationToken cancellationToken = default
    )
    {
        if (curve.Count < 3)
        {
            return null;
        }

        var info = await LoadSampleInfoAsync(sheetId, sampleId, cancellationToken).ConfigureAwait(false);
        var height = MarshallSampleFields.GetDouble(info, MarshallSampleFields.SampleInitialHeightKey);
        var context = ReadSheetContext(sheetId);
        var analysis = await LoadAnalysisSettingsAsync(cancellationToken).ConfigureAwait(false);

        var outcome = Analyze(curve, height, context, analysis);
        var columns = BuildResultColumns(outcome, context);

        // 试样记录可能已经在"试样编辑页填录"时建好了：有就更新（补曲线与结果），没有才追加
        if (sheets.ReadDetailRow(sheetId, sampleId, ["SampleID"]) is null)
        {
            await sheets.AppendSampleAsync(sheetId, sampleId, curve, columns, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await sheets.UpdateDetailFieldsAsync(sheetId, sampleId, columns, curve, cancellationToken)
                .ConfigureAwait(false);
        }

        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["load"] = outcome.PeakLoadKN,
            ["displacement"] = outcome.PeakDeformationMM,
            ["stability"] = outcome.StabilityKN,
            ["flowValue"] = outcome.FlowValueMM,
            ["originCorrection"] = outcome.OPrimeMM,
            ["marshallModulus"] = outcome.ModulusKNPerMM,
            // 下面四个键**不是指标卡**（界面按 key 找卡片，多出来的键会被忽略），
            // 是给曲线图"修正直线 / 修正原点 / 拟合区间两端"标注用的
            ["tangentK"] = outcome.TangentK,
            ["tangentB"] = outcome.TangentB,
            ["originCorrectionStart"] = analysis.StartPercent,
            ["originCorrectionEnd"] = analysis.EndPercent,
        };
    }

    public async Task CompleteSheetAsync(string sheetId, CancellationToken cancellationToken = default)
    {
        await UpdateGroupStatisticsAsync(sheetId, cancellationToken).ConfigureAwait(false);
        await sheets.CompleteAsync(sheetId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RecomputeReport> RecomputeSheetAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var analysis = await LoadAnalysisSettingsAsync(cancellationToken).ConfigureAwait(false);
        var context = ReadSheetContext(sheetId);

        var rows = sheets.ReadDetailRows(
            sheetId,
            ["SampleID", MarshallSampleFields.SampleInitialHeightKey]
        );

        var recomputed = 0;
        var skipped = 0;

        foreach (var row in rows)
        {
            var sampleId = Convert.ToString(
                row["SampleID"],
                System.Globalization.CultureInfo.InvariantCulture
            );
            if (string.IsNullOrWhiteSpace(sampleId))
            {
                continue;
            }

            var curve = sheets.ReadCurve(sheetId, sampleId);
            if (curve.Count < 3)
            {
                skipped++;
                continue;
            }

            var height = MarshallSampleFields.GetDouble(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [MarshallSampleFields.SampleInitialHeightKey] = Convert.ToString(
                        row.TryGetValue(MarshallSampleFields.SampleInitialHeightKey, out var value)
                            ? value
                            : null,
                        System.Globalization.CultureInfo.InvariantCulture
                    ) ?? string.Empty,
                },
                MarshallSampleFields.SampleInitialHeightKey
            );

            var outcome = Analyze(curve, height, context, analysis);
            await sheets
                .UpdateDetailFieldsAsync(
                    sheetId,
                    sampleId,
                    BuildResultColumns(outcome, context),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
            recomputed++;
        }

        if (recomputed > 0)
        {
            await UpdateGroupStatisticsAsync(sheetId, cancellationToken).ConfigureAwait(false);
        }

        return new RecomputeReport(recomputed, skipped);
    }

    /// <inheritdoc />
    public async Task<ValidityReport> EvaluateValidityAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var context = ReadSheetContext(sheetId);
        var (first, second) = await EvaluateGroupsAsync(sheetId, context, cancellationToken)
            .ConfigureAwait(false);

        await WriteGroupColumnsAsync(sheetId, first, second, cancellationToken).ConfigureAwait(false);

        var evaluated = first.Rows.Count + second.Rows.Count;
        var effective = first.EffectiveCount + second.EffectiveCount;
        var excluded = first
            .Rows.Where(row => !row.Effective)
            .Concat(second.Rows.Where(row => !row.Effective))
            .Select(row => row.SampleId)
            .ToArray();

        var groupsWithRows = 0;
        var groupsOk = 0;
        if (first.Rows.Count > 0)
        {
            groupsWithRows++;
            groupsOk += first.Stats is null ? 0 : 1;
        }

        if (second.Rows.Count > 0)
        {
            groupsWithRows++;
            groupsOk += second.Stats is null ? 0 : 1;
        }

        return new ValidityReport(
            evaluated,
            effective,
            excluded,
            NeedsSupplement: groupsWithRows == 0 || groupsOk < groupsWithRows
        );
    }

    /// <summary>
    /// 算两组的有效性并写主表统计 —— 「收尾一组」（<see cref="CompleteSheetAsync"/>）与
    /// 「按当前参数重算」（<see cref="RecomputeSheetAsync"/>）共用这一条路径（现役同样只有一份判定）。
    /// </summary>
    private async Task UpdateGroupStatisticsAsync(
        string sheetId,
        CancellationToken cancellationToken
    )
    {
        var context = ReadSheetContext(sheetId);
        var (first, second) = await EvaluateGroupsAsync(sheetId, context, cancellationToken)
            .ConfigureAwait(false);

        await WriteGroupColumnsAsync(sheetId, first, second, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>两组的判定（不写主表统计；调用方拿到结果再决定写什么）。</summary>
    private async Task<(GroupOutcome First, GroupOutcome Second)> EvaluateGroupsAsync(
        string sheetId,
        SheetContext context,
        CancellationToken cancellationToken
    )
    {
        var first = await EvaluateGroupAsync(sheetId, 0, context.SpecificationId, cancellationToken)
            .ConfigureAwait(false);
        var second = await EvaluateGroupAsync(sheetId, 1, context.SpecificationId, cancellationToken)
            .ConfigureAwait(false);

        return (first, second);
    }

    // ── 判定与统计的内部实现 ─────────────────────────────────────────────

    /// <summary>一张单的试验口径：试验依据 / 试件尺寸档 / 试验温度（温度逐件抄进明细）。</summary>
    private sealed record SheetContext(int SpecificationId, int SampleSizeId, string TemperatureDesc);

    private SheetContext ReadSheetContext(string sheetId)
    {
        var row = sheets.ReadMainRow(
            sheetId,
            [
                MarshallSheetFields.SpecificationKey,
                MarshallSheetFields.SampleSizeKey,
                MarshallSheetFields.TemperatureKey,
            ]
        );

        return new SheetContext(
            ReadChoice(row, MarshallSheetFields.SpecificationKey, 0),
            ReadChoice(row, MarshallSheetFields.SampleSizeKey, 0),
            Convert.ToString(
                row.TryGetValue(MarshallSheetFields.TemperatureKey, out var temperature)
                    ? temperature
                    : null,
                System.Globalization.CultureInfo.InvariantCulture
            ) ?? string.Empty
        );
    }

    /// <summary>判定用的**计算期设置**（方法级参数）：修正直线的拟合区间。</summary>
    private sealed record AnalysisSettings(double StartPercent, double EndPercent);

    private async Task<AnalysisSettings> LoadAnalysisSettingsAsync(
        CancellationToken cancellationToken
    )
    {
        if (parameters is null)
        {
            return new AnalysisSettings(
                MarshallAnalyzer.FallbackStartPercent,
                MarshallAnalyzer.FallbackEndPercent
            );
        }

        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);

        return new AnalysisSettings(
            parameters.GetDouble(values, MachineParameterCatalog.OriginCorrectionStartKey),
            parameters.GetDouble(values, MachineParameterCatalog.OriginCorrectionEndKey)
        );
    }

    /// <summary>一件试件的判定终值（明细列与指标卡都从这里取）。</summary>
    private sealed record SampleOutcome(
        MarshallAnalysisResult Analysis,
        double StabilityKN,
        double CorrectedStabilityKN,
        double FlowValueMM,
        double KFactor,
        double ModulusKNPerMM,
        double PeakLoadKN,
        double PeakDeformationMM,
        double OPrimeMM,
        double TangentK,
        double TangentB,
        bool IsSuspiciousNoPeak,
        int EndPointIndex
    );

    /// <summary>
    /// 一条曲线的判定：明显峰 / 不明显峰分流 + （芯样时）高度修正 + 模数。
    /// 分流判据照现役：曲线末端仍是最高荷载（<see cref="MarshallAnalyzer.IsCurvePeakAtEnd"/>）→ 6.2 不明显峰。
    /// </summary>
    private static SampleOutcome Analyze(
        IReadOnlyList<CurvePoint> curve,
        double heightMM,
        SheetContext context,
        AnalysisSettings analysis
    )
    {
        var isSuspicious = MarshallAnalyzer.IsCurvePeakAtEnd(curve);

        var result = isSuspicious
            ? MarshallAnalyzer.CalcNonObviousPeak(curve, analysis.StartPercent, analysis.EndPercent)
            : MarshallAnalyzer.CalcObviousPeak(curve, analysis.StartPercent, analysis.EndPercent);

        var kFactor = 0.0;
        var corrected = 0.0;
        if (context.SpecificationId == CoreSampleSpecificationId)
        {
            var size = context.SampleSizeId == 1
                ? MarshallSpecimenSize.Large150mm
                : MarshallSpecimenSize.Small100mm;

            // 越界按边缘两点外推（现役 GetMarshallCorrectionFactor(..., OOB_EXTRAPOLATE)）
            _ = MarshallCorrectionFactor.TryGet(
                heightMM,
                size,
                out kFactor,
                MarshallOutOfBoundsPolicy.Extrapolate
            );
            corrected = MarshallAnalyzer.CorrectedStability(result.MSRaw, kFactor);
        }

        var modulus = MarshallAnalyzer.MarshallModulus(
            context.SpecificationId == CoreSampleSpecificationId ? corrected : result.MSRaw,
            result.FLValue
        );

        return new SampleOutcome(
            result,
            result.MSRaw,
            corrected,
            result.FLValue,
            kFactor,
            modulus,
            result.PeakY,
            result.PeakX,
            result.OPrime,
            result.TangentK,
            result.TangentB,
            isSuspicious,
            FindPeakIndex(curve)
        );
    }

    /// <summary>曲线里荷载最大点（第一个最大）的下标 —— 写进 <c>EndPointIndex</c> 当"有效段终点"。</summary>
    private static int FindPeakIndex(IReadOnlyList<CurvePoint> curve)
    {
        var index = 0;
        var peak = double.MinValue;

        for (var i = 0; i < curve.Count; i++)
        {
            if (curve[i].Y > peak)
            {
                peak = curve[i].Y;
                index = i;
            }
        }

        return index;
    }

    /// <summary>明细行的结果列（结算写入与重算覆盖共用同一份键）。</summary>
    private static Dictionary<string, double> BuildResultColumns(
        SampleOutcome outcome,
        SheetContext context
    ) =>
        new(StringComparer.Ordinal)
        {
            ["LoadPeakValue"] = outcome.PeakLoadKN,
            ["DeformationPeakValue"] = outcome.PeakDeformationMM,
            ["MSRaw"] = outcome.StabilityKN,
            ["MSCorrected"] = outcome.CorrectedStabilityKN,
            ["KFactor"] = outcome.KFactor,
            ["FlowValue"] = outcome.FlowValueMM,
            ["MarshallModulus"] = outcome.ModulusKNPerMM,
            ["OPrime"] = outcome.OPrimeMM,
            ["PeakX"] = outcome.PeakDeformationMM,
            ["PeakY"] = outcome.PeakLoadKN,
            ["TangentK"] = outcome.TangentK,
            ["TangentB"] = outcome.TangentB,
            ["IsObvious"] = outcome.Analysis.IsObviousPeak ? 1 : 0,
            ["IsSuspiciousNoPeak"] = outcome.IsSuspiciousNoPeak ? 1 : 0,
            ["EndPointIndex"] = outcome.EndPointIndex,
            // 逐件的试验温度列留空：它是文本，而这里的结果列都是数值；
            // 报表与统计用的都是**单上**的试验温度（现役是试样弹窗里填的，本项目的口径见 MarshallSampleFields）
            ["Effective"] = 1,
            ["TestStatus"] = TestStatusCompleted,
        };

    /// <summary>一组（GroupID）的判定结果：逐件的有效性 + 组统计（有效件不足 3 件时 Stats 为 null）。</summary>
    private sealed record GroupOutcome(List<GroupRow> Rows, int EffectiveCount, GroupStats? Stats);

    private sealed record GroupRow(string SampleId, double Stability, double Flow, bool Effective);

    /// <summary>组统计：稳定度与流值各自的 均值 / 样本标准差（n−1）/ 变异系数（%）。</summary>
    private sealed record GroupStats(
        double Mean,
        double StdDev,
        double CvPercent,
        double FlowMean,
        double FlowStdDev,
        double FlowCvPercent
    );

    /// <summary>
    /// 单组判定（照现役 `UAsphaltMarshallValidity.EvaluateAndSaveGroupEffectiveness`）：
    /// 先求全组均值 → 稳定度偏离均值 15% 以上的剔除 → 重算 → 有效件 < 3 不出统计值。
    /// **有意差异**：只填了试样信息、没跑过的行（稳定度列为 NULL）**不参与**判定 ——
    /// 现役把 NULL 当 0 计入均值，会把整组拉垮。
    /// </summary>
    private async Task<GroupOutcome> EvaluateGroupAsync(
        string sheetId,
        int groupId,
        int specificationId,
        CancellationToken cancellationToken
    )
    {
        var stabilityColumn = specificationId == CoreSampleSpecificationId ? "MSCorrected" : "MSRaw";

        var rows = sheets.ReadDetailRows(
            sheetId,
            ["SampleID", "GroupID", stabilityColumn, "FlowValue"]
        );

        var groupRows = new List<(string SampleId, double Stability, double Flow)>();

        foreach (var row in rows)
        {
            if (ReadChoice(row, "GroupID", 0) != groupId)
            {
                continue;
            }

            if (!TryReadDouble(row, stabilityColumn, out var stability))
            {
                continue; // 还没跑过（NULL）或读不出来
            }

            _ = TryReadDouble(row, "FlowValue", out var flow);

            groupRows.Add(
                (
                    Convert.ToString(
                        row["SampleID"],
                        System.Globalization.CultureInfo.InvariantCulture
                    ) ?? string.Empty,
                    stability,
                    flow
                )
            );
        }

        if (groupRows.Count == 0)
        {
            return new GroupOutcome([], 0, null);
        }

        var initialMean = groupRows.Average(item => item.Stability);
        if (initialMean < 0.001)
        {
            // 现役的"除零保护"：整组近似为零时不判、不写（返回失败）
            var unchanged = groupRows
                .Select(item => new GroupRow(item.SampleId, item.Stability, item.Flow, Effective: true))
                .ToList();
            return new GroupOutcome(unchanged, 0, null);
        }

        var effective = groupRows
            .Select(item =>
                Math.Abs(item.Stability - initialMean) / initialMean <= StabilityDeviationLimit
            )
            .ToArray();

        // 逐件写回有效性（现役 Effective：True=保留 / False=舍弃）
        for (var i = 0; i < groupRows.Count; i++)
        {
            await sheets
                .UpdateDetailFieldsAsync(
                    sheetId,
                    groupRows[i].SampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["Effective"] = effective[i] ? 1 : 0,
                    },
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        var resultRows = new List<GroupRow>(groupRows.Count);
        for (var i = 0; i < groupRows.Count; i++)
        {
            resultRows.Add(
                new GroupRow(groupRows[i].SampleId, groupRows[i].Stability, groupRows[i].Flow, effective[i])
            );
        }

        var kept = resultRows.Where(row => row.Effective).ToArray();
        if (kept.Length == 0)
        {
            return new GroupOutcome(resultRows, 0, null);
        }

        var stats = ComputeStats([.. kept.Select(row => row.Stability)]);
        var flowStats = ComputeStats([.. kept.Select(row => row.Flow)]);

        return new GroupOutcome(
            resultRows,
            kept.Length,
            stats is null || flowStats is null
                ? null
                : new GroupStats(
                    stats.Value.Mean,
                    stats.Value.StdDev,
                    stats.Value.CvPercent,
                    flowStats.Value.Mean,
                    flowStats.Value.StdDev,
                    flowStats.Value.CvPercent
                )
        );
    }

    private static (double Mean, double StdDev, double CvPercent)? ComputeStats(double[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        var mean = values.Average();
        if (values.Length < 2)
        {
            return (mean, double.NaN, double.NaN);
        }

        var variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);
        var stdDev = Math.Sqrt(variance);

        return (mean, stdDev, Math.Abs(mean) > 1e-12 ? stdDev / Math.Abs(mean) * 100.0 : -1.0);
    }

    /// <summary>
    /// 把两组的统计写进主表（现役 `UpdateMainStatsInfo`：一律修约到 1 位小数）。
    ///
    /// **我们的口径是两位小数**（用户 2026-10-16：马歇尔的这些量一律"比现役多一位小数"）——
    /// 均值 / 标准差 / 变异系数 / 残留稳定度比全部 `SimpleRoundTo(…, -2)`；
    /// 界面（报表汇总区）也按两位显示。
    /// **有效件不足 3 件时不写均值**（写 null，既不下结论、也清掉上一次的陈旧值），
    /// 残留稳定度比照现役 SQLite 触发器：ROUND(二组稳定度均值 / 一组稳定度均值 × 100, 1)，
    /// 一组为空/为 0 时写 null。
    /// </summary>
    private async Task WriteGroupColumnsAsync(
        string sheetId,
        GroupOutcome first,
        GroupOutcome second,
        CancellationToken cancellationToken
    )
    {
        var columns = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Group1AvgStability"] = RoundStats(first.Stats?.Mean),
            ["Group1Stability_Std_Dev"] = RoundStats(first.Stats?.StdDev),
            ["Group1Stability_Cv_Percent"] = RoundStats(first.Stats?.CvPercent),
            ["Group1AvgFlowValue"] = RoundStats(first.Stats?.FlowMean),
            ["Group1Flow_Std_Dev"] = RoundStats(first.Stats?.FlowStdDev),
            ["Group1Flow_Cv_Percent"] = RoundStats(first.Stats?.FlowCvPercent),
            ["Group2AvgStability"] = RoundStats(second.Stats?.Mean),
            ["Group2Stability_Std_Dev"] = RoundStats(second.Stats?.StdDev),
            ["Group2Stability_Cv_Percent"] = RoundStats(second.Stats?.CvPercent),
            ["Group2AvgFlowValue"] = RoundStats(second.Stats?.FlowMean),
            ["Group2Flow_Std_Dev"] = RoundStats(second.Stats?.FlowStdDev),
            ["Group2Flow_Cv_Percent"] = RoundStats(second.Stats?.FlowCvPercent),
            ["StabilityRatio"] = StabilityRatio(first, second),
        };

        await sheets.UpdateMainFieldsAsync(sheetId, columns, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>残留稳定度比（%）：二组稳定度均值 / 一组稳定度均值 × 100，**两位小数**。</summary>
    private static double? StabilityRatio(GroupOutcome first, GroupOutcome second)
    {
        if (first.Stats is not { } group1 || second.Stats is not { } group2 || group1.Mean == 0.0)
        {
            return null;
        }

        return MarshallAnalyzer.SimpleRoundTo(group2.Mean / group1.Mean * 100.0, -2);
    }

    /// <summary>组统计的修约：**两位小数**（用户 2026-10-16 的口径，比现役多一位）。</summary>
    private static double? RoundStats(double? value) =>
        value is { } number && !double.IsNaN(number)
            ? MarshallAnalyzer.SimpleRoundTo(number, -2)
            : null;

    /// <summary>从主表行里读一个选项代号（空/异常回兜底）。</summary>
    private static int ReadChoice(
        IReadOnlyDictionary<string, object?> row,
        string column,
        int fallback
    ) =>
        row.TryGetValue(column, out var raw)
        && int.TryParse(
            Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed
        )
        && parsed >= 0
            ? parsed
            : fallback;

    private static int ReadChoice(
        IReadOnlyDictionary<string, string> fields,
        string key,
        int fallback
    ) =>
        fields.TryGetValue(key, out var text)
        && int.TryParse(
            text,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed
        )
        && parsed >= 0
            ? parsed
            : fallback;

    /// <summary>读 SheetIndex / TestStatus 这类整数列；空值/取不出来回 null。</summary>
    private static int? TryReadIndex(object? raw)
    {
        if (raw is null || raw is DBNull)
        {
            return null;
        }

        return int.TryParse(
            Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var index
        )
            ? index
            : null;
    }

    private static bool TryReadDouble(
        IReadOnlyDictionary<string, object?> row,
        string column,
        out double value
    )
    {
        value = 0;
        if (!row.TryGetValue(column, out var raw) || raw is null || raw is DBNull)
        {
            return false;
        }

        return double.TryParse(
            Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value
        );
    }
}
