namespace STS.Method.ShearStrength.Tests;

using STS.Control;

/// <summary>运行起点装配：默认值口径 + （骨架期）试样信息读取还是桩时的行为 + 尺寸解析。</summary>
public sealed class ShearStrengthExecutionTests
{
    [Fact]
    public async Task 无上下文时应给不带判据的默认profile()
    {
        var execution = new ShearStrengthExecution();

        var profile = await execution.LoadProfileAsync();

        Assert.Null(profile.StopCriteriaFactory); // 页面级"取一次口径"不挂判据
        Assert.Equal(0.0, profile.ContinueLoadingSeconds, 9); // T1236：不配继续加载
        Assert.Equal(0.0, profile.ResetSeconds, 9); // T1236：不自动归位
        Assert.Equal(0.0393, profile.ContactLoadKN, 1e-4); // 5kPa × 标准件 Φ100 面积
        Assert.Equal(1800.0, profile.LoadingTimeoutSeconds, 9);
    }

    [Fact]
    public async Task 有上下文但试样信息尚空时先不挂判据()
    {
        // 骨架期：LoadSampleInfoAsync 还是桩（回空）→ 拿不到尺寸 → 先不挂判据
        //（真实读取落地后应改为"缺尺寸 → 明确报错"，见设计文档待办①）
        var execution = new ShearStrengthExecution();

        var profile = await execution.LoadProfileAsync(new TestStartContext("WT001", "S001"));

        Assert.Null(profile.StopCriteriaFactory);
    }

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
