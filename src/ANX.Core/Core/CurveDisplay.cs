namespace ANX.Core;

/// <summary>
/// 曲线**回放显示**的修正（只影响画面，绝不改库里已存数据）。
/// </summary>
public static class CurveDisplay
{
    /// <summary>
    /// 把回放曲线的**首点 X 归零**。
    ///
    /// **为什么需要**：曲线的第一点按定义记在**位移零点**上（接触那一帧调零，X = 0）。
    /// 2026-10-13 之前的版本把首点记成了"接触帧调零**前**的位移"（启动试验时平台所在的
    /// 位置 / 上一轮遗留的偏移），于是老记录回放时曲线会从 X ≠ 0 的地方画起 —— 新记录已经
    /// 在源头修好（见 <c>TestSequencer</c> 接触分支），老记录只能靠显示层抹平。
    ///
    /// 只动首点、不动其余点，也不做整体平移：后续点本来就是相对位移零点的读数，
    /// 整体平移会把整条曲线挪歪（等于把偏移量算了两次）。
    /// </summary>
    public static IReadOnlyList<CurvePoint> ZeroFirstPointX(IReadOnlyList<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        // 空曲线没得修；首点已经在零点上（新记录本来就是）就原样返回，不白白拷贝一遍
        if (points.Count == 0 || points[0].X == 0)
        {
            return points;
        }

        var result = new CurvePoint[points.Count];
        result[0] = new CurvePoint(0, points[0].Y);
        for (var i = 1; i < points.Count; i++)
        {
            result[i] = points[i];
        }

        return result;
    }
}
