namespace ANX.Control.Tests;

using ANX.Core;
using ANX.Control;

/// <summary>
/// 两条现场保护：① 位移闸门不得超位移传感器量程；② 电机被停住时软件要自己知道。
/// 都源自 2026-10-11 真机空载试（现场量程 25mm，闸门却写 50mm，靠操作员手动停机才没顶坏传感器）。
/// </summary>
public sealed class SafetyGuardTests
{
    [Fact]
    public void 位移闸门应收敛到设备量程的安全范围内()
    {
        var profile = new TestRunProfile { MaxDisplacementMM = 50.0 };

        // 真机量程 25mm → 闸门收到 25 × 0.95 = 23.75
        var clamped = profile.ClampDisplacementToDeviceRange(25.0);
        Assert.Equal(23.75, clamped.MaxDisplacementMM, 6);

        // 设备不上报 → 原样
        Assert.Equal(50.0, profile.ClampDisplacementToDeviceRange(null).MaxDisplacementMM);

        // 设备量程比闸门宽 → 不需要收敛
        Assert.Equal(50.0, profile.ClampDisplacementToDeviceRange(100.0).MaxDisplacementMM);

        // 设备量程非法（<=0）→ 原样，避免把闸门算成 0 或负数
        Assert.Equal(50.0, profile.ClampDisplacementToDeviceRange(0).MaxDisplacementMM);

        Assert.Equal(0.95, TestRunProfile.DisplacementRangeSafetyFactor);
    }

    [Fact]
    public void 电机停转要跨过起转宽限期才判()
    {
        // 伺服起转约 1 秒（真机实测），宽限期 1.5s；确认时长 2.5s
        var guard = new MotorStallGuard(maxStalledSeconds: 2.5, startupGraceSeconds: 1.5);

        // 宽限期内即使状态是"不转"也不判（刚发指令、还没起转）
        for (var i = 0; i < 5; i++)
        {
            Assert.False(guard.Register(TestPhase.ContactSearch, motorRunning: false, deltaSeconds: 0.2));
        }

        // 宽限期之后要连续 2.5s 不转才判：1.5s 宽限 + 2.5s 确认 = 4.0s = 第 20 拍
        var stalledAt = -1;
        for (var i = 5; i < 30; i++)
        {
            if (guard.Register(TestPhase.ContactSearch, motorRunning: false, deltaSeconds: 0.2))
            {
                stalledAt = i;
                break;
            }
        }

        Assert.True(stalledAt >= 0, "连续不转超过 2.5 秒仍未判停转");
        Assert.True(stalledAt >= 19, $"判得太早：第 {stalledAt} 拍（宽限期还没走完）");
        Assert.True(guard.StalledSeconds >= 2.5);
    }

    [Fact]
    public void 电机恢复转动应清零计数()
    {
        var guard = new MotorStallGuard(maxStalledSeconds: 1.0, startupGraceSeconds: 0.0);

        Assert.False(guard.Register(TestPhase.Loading, motorRunning: false, deltaSeconds: 0.5));
        Assert.Equal(0.5, guard.StalledSeconds, 6);

        Assert.False(guard.Register(TestPhase.Loading, motorRunning: true, deltaSeconds: 0.2));
        Assert.Equal(0.0, guard.StalledSeconds, 6);

        // 只有"命令电机在转"的阶段才判：复位 / 空闲阶段电机本来就可能不转
        var idle = new MotorStallGuard(maxStalledSeconds: 0.5, startupGraceSeconds: 0.0);
        for (var i = 0; i < 10; i++)
        {
            Assert.False(idle.Register(TestPhase.Resetting, motorRunning: false, deltaSeconds: 0.2));
        }

        // 设备不上报机器状态 → 不判（不能因为读不到就当停转）
        var unknown = new MotorStallGuard(maxStalledSeconds: 0.5, startupGraceSeconds: 0.0);
        for (var i = 0; i < 10; i++)
        {
            Assert.False(unknown.Register(TestPhase.Loading, motorRunning: null, deltaSeconds: 0.2));
        }
    }
}
