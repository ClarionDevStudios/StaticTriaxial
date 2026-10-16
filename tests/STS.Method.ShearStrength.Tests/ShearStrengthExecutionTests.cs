namespace STS.Method.ShearStrength.Tests;

using STS.Control;

/// <summary>运行起点装配：无上下文时的默认值口径 + 试件尺寸解析。</summary>
public sealed class ShearStrengthExecutionTests : IDisposable
{
    private readonly TempSheetDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task 无上下文时应给不带判据的默认profile()
    {
        var execution = _database.CreateExecution();

        var profile = await execution.LoadProfileAsync();

        Assert.Null(profile.StopCriteriaFactory); // 页面级"取一次口径"不挂判据
        Assert.Equal(0.0, profile.ContinueLoadingSeconds, 9); // T1236：不配继续加载
        Assert.Equal(0.0, profile.ResetSeconds, 9); // T1236：不自动归位
        Assert.Equal(2.4, profile.JogSpeedMillimetersPerMinute, 9); // 现役点动 2400（0.001mm/min）
        Assert.Equal(0.0393, profile.ContactLoadKN, 1e-4); // 5kPa × 标准件 Φ100 面积
        Assert.Equal(1800.0, profile.LoadingTimeoutSeconds, 9);
    }

    // 「有上下文但缺尺寸」的口径已改为**硬校验报错**（设计文档待办①落地）——
    //  用例见 ShearStrengthSampleInfoTests.缺尺寸时开始试验应带键报错_有尺寸后按件装配判据与速率

    [Fact]
    public void 试件尺寸解析应读InitialHeight与InitialDiameter()
    {
        Assert.True(
            ShearStrengthExecution.TryReadSpecimenSize(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["InitialHeight"] = "306",
                    ["InitialDiameter"] = "100",
                },
                out var heightMM,
                out var diameterMM
            )
        );
        Assert.Equal(306.0, heightMM, 9);
        Assert.Equal(100.0, diameterMM, 9);

        Assert.False(
            ShearStrengthExecution.TryReadSpecimenSize(
                new Dictionary<string, string>(StringComparer.Ordinal),
                out _,
                out _
            )
        );

        Assert.False(
            ShearStrengthExecution.TryReadSpecimenSize(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["InitialHeight"] = "306" },
                out _,
                out _
            )
        );

        Assert.False(
            ShearStrengthExecution.TryReadSpecimenSize(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["InitialHeight"] = "0",
                    ["InitialDiameter"] = "100",
                },
                out _,
                out _
            )
        );
    }
}
