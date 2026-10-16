namespace STS.Core.Control;

/// <summary>
/// **围压控制能力**（可选接口，用户 2026-10-16 定）。
///
/// 框架的设备抽象 <see cref="ITestDeviceController"/> 只有 联机 / 调速 / 启停 / 点动 / 归位 / 调零，
/// 没有"围压"这个动词（沥青类机型压根没有围压系统）；土工三轴类设备额外实现本接口，
/// 运行链通过 <c>is IConfiningPressureControl</c> 判定后调用 —— 未实现它的数据来源不受影响。
///
/// 口径（用户 2026-10-16）：**施加 = 一条 C2 指令，稳压由下位机自己完成**。
/// 实现方发完指令、拿到应答即返回：**不轮询稳压状态、不做闭环调节**；
/// 上层若要等"已稳压"再继续，读 A1 的 <c>SATAE</c> 稳压位作提示即可（显示不阻塞）。
/// </summary>
public interface IConfiningPressureControl
{
    /// <summary>
    /// 施加围压 σ3（kPa）：向设备下发**一条** C2（围压目标 + 速度 + 稳压速度 + 加载杆面积）并等应答。
    /// T1236 一件试件只做一级围压（30 / 60 / 100 kPa），目标值从该件的"试验围压"来。
    /// </summary>
    Task ApplyConfiningPressureAsync(double targetKPa, CancellationToken cancellationToken = default);

    /// <summary>
    /// 卸除围压 / 反压：C2 目标归零 + 停止位（与现役"卸除压力"同帧）。
    /// 未联机时静默返回（与 <see cref="ITestDeviceController.StopAsync"/> 同语义，收尾路径上不炸）。
    /// </summary>
    Task ReleaseConfiningPressureAsync(CancellationToken cancellationToken = default);
}
