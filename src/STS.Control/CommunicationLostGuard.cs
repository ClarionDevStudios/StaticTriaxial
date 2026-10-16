namespace STS.Control;

using STS.Core;

/// <summary>
/// 通讯中断判定：连续 N 拍没有收到有效数据即判中断。
/// **联机阶段必须排除在外** —— 该阶段下位机本就不回送数据（GLT-50 明文规定联机前不发数据，
/// GR-3 也一样），把它算进"连续无数据"会把正常联机误判成通讯中断。
/// </summary>
public sealed class CommunicationLostGuard(int maxConsecutiveMisses = 50)
{
    /// <summary>不参与中断判定的阶段。</summary>
    public static readonly TestPhase[] ExemptPhases = [TestPhase.Idle, TestPhase.Connecting];

    /// <summary>连续未收到有效数据的拍数。</summary>
    public int ConsecutiveMisses { get; private set; }

    /// <summary>判定阈值（连续多少拍没数据算中断）。</summary>
    public int MaxConsecutiveMisses { get; } = maxConsecutiveMisses;

    /// <summary>登记一拍的结果；返回 true 表示应判"通讯中断"。</summary>
    public bool Register(TestPhase phase, bool dataArrived)
    {
        if (Array.IndexOf(ExemptPhases, phase) >= 0)
        {
            ConsecutiveMisses = 0;
            return false;
        }

        if (dataArrived)
        {
            ConsecutiveMisses = 0;
            return false;
        }

        ConsecutiveMisses++;
        return ConsecutiveMisses >= MaxConsecutiveMisses;
    }

    /// <summary>清零计数（重新联机时用）。</summary>
    public void Reset() => ConsecutiveMisses = 0;
}
