namespace ANX.Communication.Channels;

using ANX.Core;

/// <summary>
/// 模拟用的标准加载曲线（纯函数）：6 秒线性加载到峰值，之后 2 秒内回落 35%
/// —— 用来触发阶段机的破坏判定（荷载下降超过阈值并连续确认）。
///
/// 特意独立成文件、不放进"仅 Debug"的范围：它**不依赖模拟器设备**，
/// 回放测试与阶段机测试也要用它（否则 Release 构建会因为引用不到模拟器而编译失败）。
/// </summary>
public static class SampleCurve
{
    public static TestSample At(double seconds, double peakLoadKN, double displacementAtPeakMM)
    {
        var displacement = displacementAtPeakMM * seconds / 6.0;
        var load = seconds <= 6.0
            ? peakLoadKN * seconds / 6.0
            : peakLoadKN - peakLoadKN * 0.35 * (seconds - 6.0) / 2.0;

        return new TestSample(load, displacement, seconds);
    }
}
