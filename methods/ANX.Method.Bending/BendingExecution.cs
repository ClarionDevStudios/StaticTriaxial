namespace ANX.Method.Bending;

using ANX.Control;
using ANX.Core;
using ANX.Core.Control;
using ANX.Storage;

/// <summary>
/// T 0715 的执行实现：把界面要的能力（建单 / 判定一个试件 / 收尾一组）接到
/// 已有的判定器与存储上。界面只认 <see cref="ITestExecution"/>。
/// </summary>
public sealed class BendingExecution(TestSheetService sheets, BendingParameters? parameters = null)
    : ITestExecution, ITestSheetEditor
{
    /// <summary>单号前缀（沿用现役现场口径；以后接到设置里再改）。</summary>
    private const string NumberPrefix = "WT";

    /// <summary>
    /// 明细行的试验状态：**完成**。现役枚举 `(tsNotTested = 0, tsTesting = 1, tsCompleted = 2, tsFailed = 3)`，
    /// 试件跑完判定落库时写它（用户 2026-10-13 的置灰规则要用"这件做完了没有"）。
    /// </summary>
    private const int TestStatusCompleted = 2;

    private readonly Dictionary<string, List<BendingSampleResult>> _resultsBySheet = new(StringComparer.Ordinal);

    public string MethodCode => "T0715";

    public string MethodName => "沥青混合料弯曲试验";

    public string StandardReference => "JTG 3410-2025 T 0715-2025";

    /// <summary>曲线横轴：跨中挠度。</summary>
    public string CurveXAxisKey => BendingCurveAxes.Deflection;

    /// <summary>曲线纵轴：荷载。</summary>
    public string CurveYAxisKey => BendingCurveAxes.Load;

    public TestRunProfile Profile { get; } = new()
    {
        ContactLoadKN = 0.05,
        BreakDropKN = 0.3,
        BreakConfirmSamples = 3,
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

    /// <summary>
    /// 按参数表装配运行参数：接触阈值 / 破坏判据 / 确认帧数 / 加载速率 / 位移闸门安全系数
    /// 都取存下来的值（没存过就用参数定义的默认值）。荷载量程与超时仍用本方法的常量。
    /// </summary>
    public async Task<TestRunProfile> LoadProfileAsync(CancellationToken cancellationToken = default)
    {
        if (parameters is null)
        {
            return Profile;
        }

        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);

        return Profile with
        {
            ContactLoadKN = parameters.GetDouble(values, BendingParameters.ContactLoadKey),
            BreakDropKN = parameters.GetDouble(values, BendingParameters.BreakDropKey),
            BreakConfirmSamples = (int)
                Math.Max(1, parameters.GetDouble(values, BendingParameters.BreakConfirmKey)),
            LoadRateMillimetersPerMinute = parameters.GetDouble(
                values,
                BendingParameters.LoadRateKey
            ),
            DisplacementRangeSafetyFactor = parameters.GetDouble(
                values,
                BendingParameters.DisplacementSafetyFactorKey
            ),
            LoadRangeSafetyFactor = parameters.GetDouble(
                values,
                BendingParameters.LoadSafetyFactorKey
            ),
            JogSpeedMillimetersPerMinute = parameters.GetDouble(
                values,
                BendingParameters.JogSpeedKey
            ),
            // 参数表与 profile 都用秒（用户 2026-10-12 明确单位是秒），不做换算
            ContinueLoadingSeconds = parameters.GetDouble(
                values,
                BendingParameters.ContinueLoadingTimeKey
            ),
            MinBreakLoadKN = parameters.GetDouble(values, BendingParameters.MinBreakLoadKey),
            ResetSeconds = parameters.GetDouble(values, BendingParameters.AutoFallTimeKey),
            PressChannelNumber = (int)
                parameters.GetDouble(values, BendingParameters.PressChannelKey),
            DisplacementSource = DisplacementSources.Parse(
                (int)parameters.GetDouble(values, BendingParameters.DisplaceChannelKey)
            ),
        };
    }

    /// <summary>
    /// 试样信息字段（逐件填录）：跨径 / 试件宽 / 试件高。**不是方法级参数**（用户 2026-10-12 明确：
    /// 试件尺寸属于试样信息填录的内容），所以这里跟参数表分开声明。
    /// </summary>
    public IReadOnlyList<ParameterDefinition> SampleFields => BendingSampleFields.Definitions;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> LoadSampleInfoAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    )
    {
        var columns = BendingSampleFields.Definitions.Select(d => d.Key).ToArray();
        var row = sheets.ReadDetailRow(sheetId, sampleId, columns);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var definition in BendingSampleFields.Definitions)
        {
            // 没有该件记录（还没填过）时回落到字段默认值
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

    // ── 试验单编辑（ITestSheetEditor，用户 2026-10-14）────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<ParameterDefinition> SheetInfoFields => BendingSheetFields.Definitions;

    /// <inheritdoc />
    public Task<string> PeekNextSheetIdAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(sheets.PeekNextSheetId(MethodCode, NumberPrefix));

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadSheetInfoAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var keys = BendingSheetFields.Definitions.Select(definition => definition.Key).ToArray();
        return Task.FromResult(ToFieldTexts(sheets.ReadMainRow(sheetId, keys)));
    }

    /// <inheritdoc />
    public async Task<string> CreateSheetAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        ValidateSheetFields(fields);

        // 单号由流水生成器发（界面上显示的只是"预览号"）—— 把界面带上来的单号剔掉，避免两处不一致
        var payload = fields
            .Where(pair => !string.Equals(pair.Key, "SheetID", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        var sheetId = await sheets
            .CreateAsync(MethodCode, NumberPrefix, BuildMainColumns(payload), cancellationToken)
            .ConfigureAwait(false);

        // 单上的加载速率是"这一单实际要用的"，保存后立刻写回参数表（用户 2026-10-14）
        await PushTestConditionsAsync(sheetId, cancellationToken).ConfigureAwait(false);
        return sheetId;
    }

    /// <summary>
    /// 必填校验（方法层的事 —— 界面不认识"哪个字段必须填"）。
    /// 清单照现役 `TEditBendingTestForm.ValidatingInput`（且照现役只在**新建**时校验：
    /// `if grpSheetMode.ItemIndex &gt; 0 then Exit(True)`）：项目名称、试验规范、试验目的、试件尺寸、
    /// 混合料类型、试验温度、加载速率；体积指标 6 项**不校验**（用户 2026-10-14：允许留空）。
    /// **改已有单也走同一套校验**（用户 2026-10-14：保存前要做输入值验证）—— 比现役严，是用户要的。
    /// </summary>
    private static void ValidateSheetFields(IReadOnlyDictionary<string, string> fields)
    {
        var required = new (string Key, string Message)[]
        {
            (BendingSheetFields.ProjectNameKey, "项目名称不能为空"),
            (BendingSheetFields.SpecificationKey, "请选择试验规范"),
            (BendingSheetFields.ObjectiveKey, "请选择试验目的"),
            (BendingSheetFields.SampleSizeKey, "请选择试件尺寸"),
            (BendingSheetFields.MixtureTypeKey, "混合料类型不能为空"),
            (BendingSheetFields.TemperatureKey, "试验温度不能为空"),
            (BendingSheetFields.LoadRateKey, "加载速率不能为空"),
        };

        foreach (var (key, message) in required)
        {
            if (string.IsNullOrWhiteSpace(fields.GetValueOrDefault(key)))
            {
                throw new InvalidOperationException(message);
            }
        }

        foreach (var definition in BendingSheetFields.Definitions)
        {
            var text = fields.GetValueOrDefault(definition.Key)?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue; // 留空只有"必填"那条管得着，上面已经查过
            }

            switch (definition.Kind)
            {
                case ParameterKind.Number:
                    // 现役对温度/速率要求能转数字（TryStrToFloat / TryStrToInt）；体积指标也一样，
                    // 免得"填了 abc 却悄悄不落库"（那是最难查的假旋钮）
                    if (
                        !double.TryParse(
                            text,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var number
                        )
                    )
                    {
                        throw new InvalidOperationException($"{definition.DisplayName.TrimEnd(' ', '*')}格式不对");
                    }

                    if (
                        (definition.Minimum is { } min && number < min)
                        || (definition.Maximum is { } max && number > max)
                    )
                    {
                        throw new InvalidOperationException(
                            $"{definition.DisplayName.TrimEnd(' ', '*')}必须在 "
                                + $"{definition.Minimum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "−∞"}~"
                                + $"{definition.Maximum?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "+∞"}"
                                + $" 之间"
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
                        || definition.Options is not { } options
                        || choice < 0
                        || choice >= options.Count
                    )
                    {
                        throw new InvalidOperationException($"请选择{definition.DisplayName.TrimEnd(' ', '*')}");
                    }

                    break;
            }
        }
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

        // 留空的列要**真的变 NULL**（体积指标允许留空），所以带上空值
        await sheets
            .UpdateMainFieldsAsync(
                sheetId,
                BuildMainColumns(payload, includeBlank: true),
                cancellationToken
            )
            .ConfigureAwait(false);

        // 与新建对称：改完单，把这一单的加载速率也写回参数表（实际控制按它走）
        await PushTestConditionsAsync(sheetId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把界面填的一组字段翻译成主表列：数值列按 double、选项列按代号（0/1/2），
    /// 并补齐现役那种 `XxxId + XxxDesc` 成对写法（`TestSpecificationsDesc` / `TestObjective` / `SampleSizeDesc`）。
    /// 空值默认跳过（留给数据库 NULL，体积指标允许留空）；<paramref name="includeBlank"/> = true 时
    /// 把空值也写进列（值 = null），用于"改已有单时把原来填过的值清掉"。
    /// </summary>
    private static Dictionary<string, object?> BuildMainColumns(
        IReadOnlyDictionary<string, string> fields,
        bool includeBlank = false
    )
    {
        var columns = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var definition in BendingSheetFields.Definitions)
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

                        var label = definition.Options?.FirstOrDefault(
                            option =>
                                string.Equals(option.Value, text, StringComparison.Ordinal)
                        )?.Label;

                        if (label is { Length: > 0 } && DescriptionColumn(definition.Key) is { } desc)
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

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> ResolveDependentDefaults(
        string changedKey,
        string value
    )
    {
        if (
            !string.Equals(changedKey, BendingSheetFields.ObjectiveKey, StringComparison.Ordinal)
            || !int.TryParse(
                value,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var objective
            )
            || objective < 0
            || objective >= BendingSheetFields.ObjectiveDefaults.Length
        )
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        // 现役：常温弯拉性能 → 15℃/50；低温弯拉性能 → −10℃/50
        var (temperature, loadRate) = BendingSheetFields.ObjectiveDefaults[objective];

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [BendingSheetFields.TemperatureKey] = temperature,
            [BendingSheetFields.LoadRateKey] = loadRate,
        };
    }

    /// <inheritdoc />
    public async Task PushTestConditionsAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        if (parameters is null)
        {
            return;
        }

        var row = sheets.ReadMainRow(sheetId, [BendingSheetFields.LoadRateKey]);
        var loadRate = Convert.ToString(
            row[BendingSheetFields.LoadRateKey],
            System.Globalization.CultureInfo.InvariantCulture
        );

        if (string.IsNullOrWhiteSpace(loadRate))
        {
            return;
        }

        // 参数表按 key 合并写入（只动加载速率这一项，其余保持）
        await parameters
            .SaveAsync(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [BendingParameters.LoadRateKey] = loadRate,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<bool> HasTemplateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(
            sheets.ReadTemplateRow(MethodCode, [BendingSheetFields.ProjectNameKey]) is not null
        );

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, string>> ReadTemplateAsync(
        CancellationToken cancellationToken = default
    )
    {
        // 模板只存"下次新建时的缺省值"：单号（自动生成）与试验时间不在模板表里
        var keys = BendingSheetFields
            .Definitions.Select(definition => definition.Key)
            .Where(key => !TemplateExcludedKeys.Contains(key, StringComparer.Ordinal))
            .ToArray();

        var row = sheets.ReadTemplateRow(MethodCode, keys);

        return Task.FromResult(
            row is null ? DefaultFieldTexts() : ToFieldTexts(row)
        );
    }

    /// <inheritdoc />
    public Task SaveTemplateAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    )
    {
        // 模板只存"下次新建时的缺省值"：单号（自动生成）与试验时间不存
        var columns = BuildMainColumns(fields);
        foreach (var key in TemplateExcludedKeys)
        {
            columns.Remove(key);
        }

        return sheets.SaveTemplateAsync(MethodCode, columns, cancellationToken);
    }

    /// <summary>模板表里没有的字段（单号自动生成、试验时间每次现取）。</summary>
    private static readonly string[] TemplateExcludedKeys = ["SheetID", "CreatedOn"];

    /// <summary>选项列对应的"描述列"（现役 `XxxId + XxxDesc` 成对；试验目的的 Desc 列叫 `TestObjective`）。</summary>
    private static string? DescriptionColumn(string key) =>
        key switch
        {
            "TestSpecificationsId" => "TestSpecificationsDesc",
            "TestObjectiveId" => "TestObjective",
            "SampleSizeId" => "SampleSizeDesc",
            _ => null,
        };

    /// <summary>读回的主表行 → 界面字段文本（空值给空串，由界面决定显示）。</summary>
    private static IReadOnlyDictionary<string, string> ToFieldTexts(
        IReadOnlyDictionary<string, object?> row
    ) =>
        BendingSheetFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition =>
                row.TryGetValue(definition.Key, out var value) && value is not null
                    ? Convert.ToString(
                        value,
                        System.Globalization.CultureInfo.InvariantCulture
                    ) ?? string.Empty
                    : string.Empty,
            StringComparer.Ordinal
        );

    /// <summary>各字段的默认值（没有模板时用）。</summary>
    private static IReadOnlyDictionary<string, string> DefaultFieldTexts() =>
        BendingSheetFields.Definitions.ToDictionary(
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
            // 「有效性计算」按钮要看"这件有没有判定值"——按判定值判断，不只看状态列
            "FlexuralStrength",
            "MaxBendingTensileStrain",
        }
            .Concat(BendingSampleFields.Definitions.Select(definition => definition.Key))
            .ToArray();

        // 按序号排（SheetIndex 就是试件号里的序号）；老记录该列为空 → 从试件号里解析序号
        // （否则那些行会被当成"没有序号"甩到最后，新旧混排时看着像乱序）
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
                row => Convert.ToString(row["SampleID"], System.Globalization.CultureInfo.InvariantCulture),
                StringComparer.Ordinal
            )
            .ToArray();

        var result = new List<SampleRowInfo>(rows.Length);

        foreach (var row in rows)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var definition in BendingSampleFields.Definitions)
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
                    // 判定值齐 = 弯拉强度与最大弯拉应变都能读出来（与 EvaluateValidityAsync 的判定口径一致）
                    TryReadDouble(row, "FlexuralStrength", out _)
                        && TryReadDouble(row, "MaxBendingTensileStrain", out _),
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
        var values = BendingSampleFields.Definitions.ToDictionary(
            definition => definition.Key,
            definition => BendingSampleFields.GetDouble(fields, definition.Key),
            StringComparer.Ordinal
        );

        return sheets.UpsertSampleInfoAsync(sheetId, sampleId, values, cancellationToken);
    }

    /// <summary>实时指标卡：与现役执行页的 7 个卡片同构，按本方法取用。</summary>
    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        // 荷载 / 位移是实时跳动的读数：数值区预留 8 位宽，免得卡片随位数变宽变窄
        new("load", BendingMetricKeys.Load, "kN", ShowPeak: true, ValueMinChars: 8),
        new("displacement", BendingMetricKeys.Displacement, "mm", ShowPeak: true, ValueMinChars: 8),
        new("flexuralStrength", BendingMetricKeys.FlexuralStrength, "MPa"),
        new("originCorrection", BendingMetricKeys.OriginCorrection, "mm"),
        new("maxStrain", BendingMetricKeys.MaxStrain, "με"),
        new("correctedDeflection", BendingMetricKeys.CorrectedDeflection, "mm"),
        new("elapsed", BendingMetricKeys.Elapsed, "s"),
    ];

    public Task<string> CreateSheetAsync(
        string projectName,
        string numberPrefix,
        CancellationToken cancellationToken = default
    ) => sheets.CreateAsync(MethodCode, projectName, numberPrefix, cancellationToken);

    /// <summary>下一个试件号 = 现有明细里最大的数字号 + 1（没有则 1）。</summary>
    public Task<string> NextSampleIdAsync(string sheetId, CancellationToken cancellationToken = default)
    {
        // 试件号 = "S" + 三位序号（S001…），序号取该单已有的最大值 + 1；单里还没试件就是 S001。
        // 现役同口径：`GetMaxSampleIndex`（MAX(SheetIndex)+1）+ `'S' + FormatFloat('000', …)`
        // （用户 2026-10-13 要求照现役自动生成）。
        var rows = sheets.ReadDetailRows(sheetId, ["SampleID", "SheetIndex"]);

        var max = 0;
        foreach (var row in rows)
        {
            // 首选 SheetIndex；老记录这一列是空的（或干脆没有），退回从试件号里解析序号
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

        // 尺寸从**这一件试样自己的信息**取（试样编辑页填录、存在明细行里），不再从方法级参数取
        var info = await LoadSampleInfoAsync(sheetId, sampleId, cancellationToken).ConfigureAwait(false);
        var span = BendingSampleFields.GetDouble(info, BendingSampleFields.SpanLengthKey);
        var width = BendingSampleFields.GetDouble(info, BendingSampleFields.SampleWidthKey);
        var height = BendingSampleFields.GetDouble(info, BendingSampleFields.SampleHeightKey);
        var analysis = await LoadAnalysisSettingsAsync(cancellationToken).ConfigureAwait(false);

        var result = BendingAnalyzer.Analyze(
            curve,
            span,
            width,
            height,
            analysis.EnableOriginCorrection,
            analysis.StartPercent,
            analysis.EndPercent,
            analysis.LockPeakDisplacement
        );
        var columns = BuildResultColumns(width, height, result);

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

        if (!_resultsBySheet.TryGetValue(sheetId, out var list))
        {
            list = [];
            _resultsBySheet[sheetId] = list;
        }

        list.Add(result);

        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["load"] = result.PeakLoadKN,
            ["displacement"] = result.PeakDeflectionMM,
            ["flexuralStrength"] = result.FlexuralStrengthMPa,
            ["originCorrection"] = result.OriginCorrectionMM,
            ["maxStrain"] = result.MaxBendingTensileStrainMicro,
            ["correctedDeflection"] = result.CorrectedDeflectionMM,
            // 下面四个键**不是指标卡**（界面按 key 找卡片，多出来的键会被忽略），
            // 是给曲线图"修正直线 / 修正原点 / 拟合区间两端"标注用的（用户 2026-10-13）：
            // 直线 y = k·x + b；关掉原点修正时判定器不拟合，k = b = 0。
            ["tangentK"] = result.TangentK,
            ["tangentB"] = result.TangentB,
            ["originCorrectionStart"] = analysis.StartPercent,
            ["originCorrectionEnd"] = analysis.EndPercent,
        };
    }

    public async Task CompleteSheetAsync(string sheetId, CancellationToken cancellationToken = default)
    {
        if (_resultsBySheet.TryGetValue(sheetId, out var results) && results.Count > 0)
        {
            var group = BendingAnalyzer.AnalyzeGroup(results);

            await sheets.UpdateMainFieldsAsync(sheetId, BuildGroupColumns(group), cancellationToken)
                .ConfigureAwait(false);
        }

        await sheets.CompleteAsync(sheetId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RecomputeReport> RecomputeSheetAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        var analysis = await LoadAnalysisSettingsAsync(cancellationToken).ConfigureAwait(false);

        // 每件按**它自己的试样信息**重算（尺寸是逐件的，不能拿一份全局尺寸套所有件）
        var rows = sheets.ReadDetailRows(
            sheetId,
            [
                "SampleID",
                BendingSampleFields.SpanLengthKey,
                BendingSampleFields.SampleWidthKey,
                BendingSampleFields.SampleHeightKey,
            ]
        );

        var recomputed = new List<BendingSampleResult>();
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
                skipped++; // 曲线不足以判定（点太少）
                continue;
            }

            var info = BendingSampleFields.Definitions.ToDictionary(
                definition => definition.Key,
                definition => Convert.ToString(
                    row.TryGetValue(definition.Key, out var value) ? value : null,
                    System.Globalization.CultureInfo.InvariantCulture
                ) ?? string.Empty,
                StringComparer.Ordinal
            );

            var span = BendingSampleFields.GetDouble(info, BendingSampleFields.SpanLengthKey);
            var width = BendingSampleFields.GetDouble(info, BendingSampleFields.SampleWidthKey);
            var height = BendingSampleFields.GetDouble(info, BendingSampleFields.SampleHeightKey);

            BendingSampleResult result;
            try
            {
                result = BendingAnalyzer.Analyze(
                    curve,
                    span,
                    width,
                    height,
                    analysis.EnableOriginCorrection,
                    analysis.StartPercent,
                    analysis.EndPercent,
                    analysis.LockPeakDisplacement
                );
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                skipped++; // 单件判定不了（例如峰值非正、上升段不足）不该连累整单
                continue;
            }

            await sheets
                .UpdateDetailFieldsAsync(
                    sheetId,
                    sampleId,
                    BuildResultColumns(width, height, result),
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);

            recomputed.Add(result);
        }

        if (recomputed.Count > 0)
        {
            // 组统计同样按新结果重算（与"完成一组"同口径）
            _resultsBySheet[sheetId] = recomputed;
            await sheets
                .UpdateMainFieldsAsync(
                    sheetId,
                    BuildGroupColumns(BendingAnalyzer.AnalyzeGroup(recomputed)),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        return new RecomputeReport(recomputed.Count, skipped);
    }

    /// <summary>判定用的**计算期设置**（方法级参数）；尺寸不在这里（属试样信息，逐件取）。</summary>
    private sealed record AnalysisSettings(
        bool EnableOriginCorrection,
        double StartPercent,
        double EndPercent,
        bool LockPeakDisplacement
    );

    private async Task<AnalysisSettings> LoadAnalysisSettingsAsync(CancellationToken cancellationToken)
    {
        if (parameters is null)
        {
            return new AnalysisSettings(true, 10.0, 80.0, true);
        }

        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);

        return new AnalysisSettings(
            parameters.GetDouble(values, BendingParameters.EnableOriginCorrectionKey) != 0,
            parameters.GetDouble(values, BendingParameters.OriginCorrectionStartKey),
            parameters.GetDouble(values, BendingParameters.OriginCorrectionEndKey),
            parameters.GetDouble(values, MachineParameterCatalog.LockDisplacementKey) != 0
        );
    }

    /// <summary>明细行的结果列（结算写入与重算覆盖共用同一份键）。</summary>
    private static Dictionary<string, double> BuildResultColumns(
        double widthMM,
        double heightMM,
        BendingSampleResult result
    ) =>
        new(StringComparer.Ordinal)
        {
            ["SampleWidth"] = widthMM,
            ["SampleHeight"] = heightMM,
            ["PeakLoadValue"] = result.PeakLoadKN,
            ["PeakDeflection"] = result.PeakDeflectionMM,
            ["OriginCorrection"] = result.OriginCorrectionMM,
            ["CorrectedDeflection"] = result.CorrectedDeflectionMM,
            ["TangentK"] = result.TangentK,
            ["TangentB"] = result.TangentB,
            ["FlexuralStrength"] = result.FlexuralStrengthMPa,
            ["MaxBendingTensileStrain"] = result.MaxBendingTensileStrainMicro,
            ["IsEffective"] = 1,
            // 这一件做完了（现役 TestStatus = 2 完成）：界面据此不再让改它的试样信息
            ["TestStatus"] = TestStatusCompleted,
        };

    /// <summary>
    /// 主表上的组统计列。
    ///
    /// **有效件不足 3 件时不写组结果**（用户 2026-10-13，规范 5.6 + 现役"本次不写组结果"）：
    /// 均值列写 <c>null</c> —— 既不下结论、也**清掉上一次算出来的陈旧均值**（否则报表会拿旧数当真）；
    /// 件数与"需补做"标志照写，界面与报表据此显示"有效 N 件、需补做"。
    /// </summary>
    private static Dictionary<string, object?> BuildGroupColumns(BendingGroupResult group) =>
        new(StringComparer.Ordinal)
        {
            ["FlexuralStrengthAvg"] = group.NeedsSupplement
                ? null
                : group.MeanFlexuralStrengthMPa,
            ["MaxBendingStrainAvg"] = group.NeedsSupplement
                ? null
                : group.MeanMaxBendingTensileStrainMicro,
            ["EffectiveCount"] = group.EffectiveCount,
            ["ExcludedCount"] = group.ExcludedIndexes.Count,
            ["NeedsSupplement"] = group.NeedsSupplement ? 1 : 0,
        };

    /// <inheritdoc />
    public async Task<ValidityReport> EvaluateValidityAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    )
    {
        // 从**库里读**已存的判定结果（不只认本会话的内存结果），这样昨天那单也能算。
        var rows = sheets.ReadDetailRows(
            sheetId,
            [
                "SampleID",
                "FlexuralStrength",
                "MaxBendingTensileStrain",
                "PeakLoadValue",
                "PeakDeflection",
                "CorrectedDeflection",
                "TangentK",
                "TangentB",
                "OriginCorrection",
            ]
        );

        var samples = new List<BendingSampleResult>();
        var sampleIds = new List<string>();

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

            // 还没判定出结果的试件（没跑完 / 判定失败）不参与有效性判定
            if (!TryReadDouble(row, "FlexuralStrength", out var strength)
                || !TryReadDouble(row, "MaxBendingTensileStrain", out var strain))
            {
                continue;
            }

            TryReadDouble(row, "PeakLoadValue", out var peakLoad);
            TryReadDouble(row, "PeakDeflection", out var peakDeflection);
            TryReadDouble(row, "CorrectedDeflection", out var correctedDeflection);
            TryReadDouble(row, "TangentK", out var tangentK);
            TryReadDouble(row, "TangentB", out var tangentB);
            TryReadDouble(row, "OriginCorrection", out var originCorrection);

            samples.Add(
                new BendingSampleResult(
                    peakLoad,
                    peakDeflection,
                    correctedDeflection,
                    strength,
                    strain,
                    tangentK,
                    tangentB,
                    originCorrection
                )
            );
            sampleIds.Add(sampleId);
        }

        if (samples.Count == 0)
        {
            return new ValidityReport(0, 0, [], NeedsSupplement: true);
        }

        var group = BendingAnalyzer.AnalyzeGroup(samples);
        var excludedIds = group
            .ExcludedIndexes.Select(index => sampleIds[index])
            .ToArray();
        var excludedSet = new HashSet<string>(excludedIds, StringComparer.Ordinal);

        // 逐件写回有效性（现役 Effective：True=保留 / False=舍弃）
        foreach (var sampleId in sampleIds)
        {
            await sheets
                .UpdateDetailFieldsAsync(
                    sheetId,
                    sampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["IsEffective"] = excludedSet.Contains(sampleId) ? 0 : 1,
                    },
                    cancellationToken: cancellationToken
                )
                .ConfigureAwait(false);
        }

        await sheets
            .UpdateMainFieldsAsync(sheetId, BuildGroupColumns(group), cancellationToken)
            .ConfigureAwait(false);

        return new ValidityReport(
            samples.Count,
            group.EffectiveCount,
            excludedIds,
            group.NeedsSupplement
        );
    }

    /// <summary>把明细行里的数值读成 double（NULL / 空值返回 false）。</summary>
    /// <summary>读 SheetIndex 列；空值/取不出来回 null（老记录这一列是空的）。</summary>
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
