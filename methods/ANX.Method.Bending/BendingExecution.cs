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
    : ITestExecution
{
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
        var rows = sheets.ReadDetailRows(sheetId, ["SampleID"]);
        var max = 0;
        foreach (var row in rows)
        {
            var text = Convert.ToString(row["SampleID"], System.Globalization.CultureInfo.InvariantCulture);
            if (int.TryParse(text, out var value) && value > max)
            {
                max = value;
            }
        }

        return Task.FromResult((max + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        if (sheets.ReadDetailRow(sheetId, sampleId, ["SampleKey"]) is null)
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

            var curve = sheets.ReadCurve($"{sheetId}-{sampleId}");
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
        };

    /// <summary>主表上的组统计列。</summary>
    private static Dictionary<string, double> BuildGroupColumns(BendingGroupResult group) =>
        new(StringComparer.Ordinal)
        {
            ["FlexuralStrengthAvg"] = group.MeanFlexuralStrengthMPa,
            ["MaxBendingStrainAvg"] = group.MeanMaxBendingTensileStrainMicro,
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
