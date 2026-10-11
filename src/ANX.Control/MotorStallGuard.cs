namespace ANX.Control;

using ANX.Core;

/// <summary>
/// 电机停转侦测：**命令在找接触 / 加载，而机器状态连续若干秒显示电机不转** → 判"设备已停"。
/// </summary>
/// <remarks>
/// 现场用例（2026-10-11 真机空载试）：操作员担心顶到位移传感器量程，**手动停机**，
/// 而软件仍在 ContactSearch 里傻等接触，直到人工从界面按停止 —— 必须让软件自己知道。
/// 同类情况还有：撞限位、伺服保护、下位机自己停机。
///
/// 两个阈值都要留余量：**伺服起转约需 1 秒**（真机实测），所以进入阶段后先给一段宽限期；
/// 再用一段持续时间确认，避免单帧抖动误判。
/// </remarks>
public sealed class MotorStallGuard(
    double maxStalledSeconds = 2.5,
    double startupGraceSeconds = 1.5
)
{
    /// <summary>参与判定的阶段：只有"命令电机在转"的阶段才谈得上停转。</summary>
    public static readonly TestPhase[] WatchedPhases = [TestPhase.ContactSearch, TestPhase.Loading];

    private TestPhase _phase = TestPhase.Idle;
    private double _phaseSeconds;
    private double _stalledSeconds;

    /// <summary>已累计的"电机没转"时长（s）。</summary>
    public double StalledSeconds => _stalledSeconds;

    /// <summary>确认停转所需的持续时长（s）。</summary>
    public double MaxStalledSeconds { get; } = maxStalledSeconds;

    /// <summary>进入阶段后的宽限期（s），用于跨过伺服起转延迟。</summary>
    public double StartupGraceSeconds { get; } = startupGraceSeconds;

    /// <summary>喂一拍；返回 true 表示应判"设备已停"。</summary>
    public bool Register(TestPhase phase, bool? motorRunning, double deltaSeconds)
    {
        if (phase != _phase)
        {
            _phase = phase;
            _phaseSeconds = 0;
            _stalledSeconds = 0;
        }

        _phaseSeconds += deltaSeconds;

        if (
            Array.IndexOf(WatchedPhases, phase) < 0
            || motorRunning is not false
            || _phaseSeconds < StartupGraceSeconds
        )
        {
            _stalledSeconds = 0;
            return false;
        }

        _stalledSeconds += deltaSeconds;
        return _stalledSeconds >= MaxStalledSeconds;
    }

    /// <summary>清零（重新开始一轮时用）。</summary>
    public void Reset()
    {
        _phase = TestPhase.Idle;
        _phaseSeconds = 0;
        _stalledSeconds = 0;
    }
}
