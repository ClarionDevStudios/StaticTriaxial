namespace STS.Core;

/// <summary>
/// 试验阶段机的阶段。与现役控制线程的 <c>TTestPhase</c> 对应，用于界面显示与控制流分支。
/// </summary>
public enum TestPhase
{
    /// <summary>空闲，未开始。</summary>
    Idle = 0,

    /// <summary>联机中（下位机在此阶段通常不回送数据，不可按"通讯中断"判）。</summary>
    Connecting,

    /// <summary>接触搜索（荷载达到接触阈值后才开始记录曲线）。</summary>
    ContactSearch,

    /// <summary>加载中。</summary>
    Loading,

    /// <summary>破坏确认中（荷载下降超过阈值后连续 N 帧确认）。</summary>
    Breaking,

    /// <summary>
    /// 破坏后继续加载中。目的是**让曲线走出一段破坏后荷载下降的趋势**（用户 2026-10-12 说明）。
    /// 电机仍按试验速率前进，上位机不发新指令；这段时间**记曲线**。
    /// </summary>
    ContinueLoading,

    /// <summary>归位中（电机反转把工作台送回起始位）。这段**不记曲线**。</summary>
    Resetting,

    /// <summary>正常完成，可结算。</summary>
    Completed,

    /// <summary>异常中止（保护动作 / 人工停止 / 通讯中断）。</summary>
    Aborted,
}
