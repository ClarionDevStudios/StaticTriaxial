namespace ANX.Core;

/// <summary>一次试验的停止原因。判定"可否结算"时与曲线是否为空一起看。</summary>
public enum TestStopReason
{
    None = 0,

    /// <summary>正常完成（破坏确认成立）。</summary>
    Completed,

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
