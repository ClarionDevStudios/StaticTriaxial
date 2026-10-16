namespace STS.Core;

/// <summary>一次试验的停止原因。判定"可否结算"时与曲线是否为空一起看。</summary>
public enum TestStopReason
{
    None = 0,

    /// <summary>正常完成（破坏确认成立）。</summary>
    Completed,

    /// <summary>
    /// **接触段完成**（T1236 分段流程的阶段边界，用户 2026-10-16 定）：3~5kPa 正接触成立即停，
    /// 留给"施加围压 → 稳压 → 开始剪切"两段；这一轮不结算。
    /// </summary>
    ContactEstablished,

    /// <summary>人工停止。</summary>
    ManualStop,

    /// <summary>荷载超出量程。</summary>
    LoadRangeExceeded,

    /// <summary>位移超出正向行程。</summary>
    DisplacementRangeExceeded,

    /// <summary>接触阶段超时。</summary>
    ContactTimeout,

    /// <summary>加载阶段超时。</summary>
    LoadingTimeout,

    /// <summary>通讯中断。</summary>
    CommunicationLost,

    /// <summary>设备报错。</summary>
    DeviceError,
}
