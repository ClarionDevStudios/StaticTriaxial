namespace ANX.Method.Bending;

using ANX.Control;
using ANX.Core;
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
        var enableOriginCorrection = await LoadOriginCorrectionEnabledAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = BendingAnalyzer.Analyze(curve, span, width, height, enableOriginCorrection);
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
        var enableOriginCorrection = await LoadOriginCorrectionEnabledAsync(cancellationToken)
            .ConfigureAwait(false);

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
                result = BendingAnalyzer.Analyze(curve, span, width, height, enableOriginCorrection);
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

    /// <summary>
    /// 判定用的**计算期开关**：是否做曲线原点修正（方法级参数）。
    /// 尺寸不在这里 —— 它们属于**试样信息**，逐件从明细行取（见 <see cref="LoadSampleInfoAsync"/>）。
    /// </summary>
    private async Task<bool> LoadOriginCorrectionEnabledAsync(CancellationToken cancellationToken)
    {
        if (parameters is null)
        {
            return true;
        }

        var values = await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);
        return parameters.GetDouble(values, BendingParameters.EnableOriginCorrectionKey) != 0;
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
}
