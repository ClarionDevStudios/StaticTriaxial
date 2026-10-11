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
    };

    /// <summary>
    /// 按参数表装配运行参数：接触阈值 / 破坏判据 / 确认帧数 / 加载速率都取存下来的值
    /// （没存过就用参数定义的默认值）。量程与超时仍用本方法的常量。
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
        };
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

        // 试件尺寸与跨径从参数配置里读（试样编辑页可改），不再硬编码
        var values = parameters is null
            ? null
            : await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);
        var span = values is null ? 200.0 : parameters!.GetDouble(values, BendingParameters.SpanLengthKey);
        var width = values is null ? 30.0 : parameters!.GetDouble(values, BendingParameters.SampleWidthKey);
        var height = values is null ? 35.0 : parameters!.GetDouble(values, BendingParameters.SampleHeightKey);

        var result = BendingAnalyzer.Analyze(curve, span, width, height);

        await sheets
            .AppendSampleAsync(
                sheetId,
                sampleId,
                curve,
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleWidth"] = width,
                    ["SampleHeight"] = height,
                    ["PeakLoadValue"] = result.PeakLoadKN,
                    ["PeakDeflection"] = result.PeakDeflectionMM,
                    ["OriginCorrection"] = result.OriginCorrectionMM,
                    ["CorrectedDeflection"] = result.CorrectedDeflectionMM,
                    ["TangentK"] = result.TangentK,
                    ["TangentB"] = result.TangentB,
                    ["FlexuralStrength"] = result.FlexuralStrengthMPa,
                    ["MaxBendingTensileStrain"] = result.MaxBendingTensileStrainMicro,
                    ["IsEffective"] = 1,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

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

            await sheets
                .UpdateMainFieldsAsync(
                    sheetId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["FlexuralStrengthAvg"] = group.MeanFlexuralStrengthMPa,
                        ["MaxBendingStrainAvg"] = group.MeanMaxBendingTensileStrainMicro,
                        ["EffectiveCount"] = group.EffectiveCount,
                        ["ExcludedCount"] = group.ExcludedIndexes.Count,
                        ["NeedsSupplement"] = group.NeedsSupplement ? 1 : 0,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        await sheets.CompleteAsync(sheetId, cancellationToken).ConfigureAwait(false);
    }
}
