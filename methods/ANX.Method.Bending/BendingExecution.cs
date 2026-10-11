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
    };

    /// <summary>实时指标卡：与现役执行页的 7 个卡片同构，按本方法取用。</summary>
    public IReadOnlyList<MetricDefinition> Metrics { get; } =
    [
        new("load", "荷载", "kN", ShowPeak: true),
        new("displacement", "位移", "mm", ShowPeak: true),
        new("flexuralStrength", "弯拉强度", "MPa"),
        new("originCorrection", "修正原点", "mm"),
        new("maxStrain", "最大弯拉应变", "με"),
        new("correctedDeflection", "跨中挠度", "mm"),
        new("elapsed", "试验持续时间", "s"),
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
