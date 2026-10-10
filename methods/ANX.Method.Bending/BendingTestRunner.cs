namespace ANX.Method.Bending;

using ANX.Control;
using ANX.Core;
using ANX.Core.Control;
using ANX.Storage;

/// <summary>一个试件的运行参数。</summary>
public sealed record BendingSampleSpec(
    string SampleId,
    double SpanMM,
    double WidthMM,
    double HeightMM
);

/// <summary>
/// 跑一个试件：启动设备 → 阶段机吃到结束 → 用曲线算判定 → 落一条明细行；
/// 跑完一组再算组统计并写回主表。
/// 组合的都是已有件（设备 + 阶段机 + 判定 + 存储），不引入新机制。
/// </summary>
public sealed class BendingTestRunner(
    TestSheetService sheets,
    ITestDeviceController device,
    TestRunProfile? profile = null
)
{
    private readonly TestRunProfile _profile = profile ?? new TestRunProfile();

    /// <summary>设备是否已联机（未联机时先联机）。</summary>
    public async Task EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        if (!device.IsConnected)
        {
            await device.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 跑一个试件并落库。
    /// </summary>
    /// <returns>判定结果；若试验未能正常结算（超时 / 保护动作 / 人工停止）返回 null 且不落库。</returns>
    public async Task<BendingSampleResult?> RunSampleAsync(
        string sheetId,
        BendingSampleSpec spec,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        var sequencer = new TestSequencer(_profile);
        var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnSample(object? sender, TestSample sample)
        {
            if (!sequencer.OnSample(sample))
            {
                finished.TrySetResult(true);
            }
        }

        sequencer.Start();
        device.SampleReceived += OnSample;

        try
        {
            await device.StartAsync(cancellationToken).ConfigureAwait(false);

            var completed = await Task
                .WhenAny(
                    finished.Task,
                    Task.Delay(timeout ?? TimeSpan.FromMinutes(5), cancellationToken)
                )
                .ConfigureAwait(false);

            if (completed != finished.Task)
            {
                sequencer.Stop(TestStopReason.LoadingTimeout);
            }
        }
        finally
        {
            device.SampleReceived -= OnSample;
            await device.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!sequencer.CanSettle)
        {
            return null;
        }

        var result = BendingAnalyzer.Analyze(
            sequencer.Curve,
            spec.SpanMM,
            spec.WidthMM,
            spec.HeightMM
        );

        await sheets
            .AppendSampleAsync(
                sheetId,
                spec.SampleId,
                sequencer.Curve,
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleWidth"] = spec.WidthMM,
                    ["SampleHeight"] = spec.HeightMM,
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

        return result;
    }

    /// <summary>跑完一组：算组统计（含 5.6 超差剔除）写回主表，并把试验单标记完成。</summary>
    public async Task<BendingGroupResult> FinishGroupAsync(
        string sheetId,
        IReadOnlyList<BendingSampleResult> samples,
        CancellationToken cancellationToken = default
    )
    {
        var group = BendingAnalyzer.AnalyzeGroup(samples);

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

        await sheets.CompleteAsync(sheetId, cancellationToken).ConfigureAwait(false);
        return group;
    }
}
