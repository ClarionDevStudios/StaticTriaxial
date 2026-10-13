namespace ANX.Core;

/// <summary>
/// 曲线**显示**用的小工具（回放修正与取点；都只影响画面，绝不改库里已存数据）。
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

    /// <summary>
    /// 在曲线的**上升段**（首点 → 峰值点）上找荷载等于 <paramref name="loadKN"/> 的那一点。
    ///
    /// **为什么要插值**：采样是 20ms 一拍，直接取"第一个超过该荷载的点"会带最多半拍的偏差；
    /// 10% / 80% 这两个点是**拟合区间的上下沿**，画在曲线上要落在准确位置。
    /// （用户 2026-10-13 更正：这两个点标在**曲线**上，不是标在修正直线上。）
    /// </summary>
    /// <returns>
    /// 交点（<c>Y</c> 取 <paramref name="loadKN"/>）；曲线上升段没经过该荷载时回 <c>null</c>
    /// —— 峰值低于该荷载，或空曲线。首点就已经高于该荷载时回**首点**（起步很陡，没有可插值的区间）。
    /// </returns>
    public static CurvePoint? PointAtLoad(IReadOnlyList<CurvePoint> points, double loadKN)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return null;
        }

        // 峰值（Y 最大）那个点的下标：只找它之前的上升段，别把破坏后的下降段算进来
        var peakIndex = 0;
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].Y > points[peakIndex].Y)
            {
                peakIndex = i;
            }
        }

        if (points[peakIndex].Y < loadKN)
        {
            return null; // 整条曲线都到不了这个荷载
        }

        if (points[0].Y >= loadKN)
        {
            return points[0];
        }

        for (var i = 1; i <= peakIndex; i++)
        {
            if (points[i].Y < loadKN)
            {
                continue;
            }

            var prev = points[i - 1];
            var delta = points[i].Y - prev.Y;
            if (delta <= 0)
            {
                return points[i]; // 不升反降（理论上不出现）：保守回当前点，不插值
            }

            var t = (loadKN - prev.Y) / delta;
            return new CurvePoint(prev.X + ((points[i].X - prev.X) * t), loadKN);
        }

        return points[peakIndex];
    }
}
