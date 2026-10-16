namespace STS.Control;

/// <summary>停止判定结果（方法判据 → 阶段机）。<paramref name="Detail"/> 供日志 / 界面文案用（可空）。</summary>
public sealed record TestStopDecision(bool ShouldStop, string? Detail = null);

/// <summary>
/// 方法级"停止 / 破坏判据"：加载阶段**逐拍**询问（每次采样一次）。
/// 判据只回答"要不要停"；停后的收尾（继续加载 / 归位 / 直接完成）由
/// <see cref="TestRunProfile"/> 的 ContinueLoadingSeconds / ResetSeconds 管。
///
/// 为什么经"工厂"发放：判据是**有状态的状态机**（例如 T 1236 的 0.5mm 增量窗口），
/// 每轮试验必须换新的；<see cref="TestRunProfile.StopCriteriaFactory"/> 在每次
/// <c>TestSequencer.Start()</c> 时现调一次。
/// </summary>
public interface ITestStopCriteria
{
    /// <summary>
    /// 喂一拍采样：位移取**接触调零后的净值**（与曲线同一口径，mm），
    /// 荷载为当前轴向荷载（kN），elapsed 从试验开始累计（s）。
    /// </summary>
    TestStopDecision Evaluate(double displacementMM, double loadKN, double elapsedSeconds);
}
