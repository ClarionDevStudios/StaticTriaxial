namespace ANX.Core;

/// <summary>
/// 曲线**显示**用的小工具（回放修正与取点；都只影响画面，绝不改库里已存数据）。
/// </summary>
public static class CurveDisplay
{
    /// <summary>
    /// 把曲线开头"**位移还没归零**"的点去掉，并让首点落在 X = 0 上。
    ///
    /// **为什么要去掉**（用户 2026-10-15）：接触那一帧调零**之前 / 还没生效**的那几个点，
    /// X 还停在旧偏移上（实测老记录是 1.2~10.4mm），调零生效后的点才从 0.00x 起。
    /// 这些点在图上表现为"从 0 跳到旧偏移、再跳回来"的一段折线，看着像噪声。
    /// 2026-10-13 起新记录已在**源头**修好（见 <c>TestSequencer</c> 接触分支）；
    /// 老记录库里做过一次性清理，这里留一层兜底 —— 别的机器 / 漏网的记录也不会画歪。
    ///
    /// **判定不受影响**：这些点都落在荷载 ≈ 0 的位置，实测裁前裁后峰值 / 强度 / 应变一模一样
    /// （老记录里 6 件、共裁 12 点：`WT20261013003/1` 强度 81.61MPa、应变 11090.4με 裁前裁后相同）。
    ///
    /// 只裁开头、不动其余点，也**不做整体平移**：后续点本来就是相对位移零点的读数，
    /// 整体平移会把整条曲线挪歪（等于把偏移量算了两次）。
    /// </summary>
    /// <param name="points">原始曲线（库里存的）。</param>
    /// <param name="toleranceMM">
    /// 多大算"已经归零"。实测调零后从 0.002~0.019mm 起、旧偏移是 1.25~10.4mm，
    /// 0.1mm 稳稳落在两者中间。
    /// </param>
    public static IReadOnlyList<CurvePoint> TrimUnzeroedStart(
        IReadOnlyList<CurvePoint> points,
        double toleranceMM = 0.1
    )
    {
        ArgumentNullException.ThrowIfNull(points);

        // 空曲线没得改
        if (points.Count == 0)
        {
            return points;
        }

        var firstZero = -1;
        for (var i = 0; i < points.Count; i++)
        {
            if (Math.Abs(points[i].X) <= toleranceMM)
            {
                firstZero = i;
                break;
            }
        }

        // 找不到"已归零"的点 → 可能是整条曲线本来就远离 0（不拿它当异常数据），**原样返回**
        // 裁完只剩不到 2 点也不动 —— 宁可难看，也不能把数据裁没了
        if (firstZero < 0 || firstZero >= points.Count - 1)
        {
            return points;
        }

        // 首点已经在零点上、且前面没有要裁的：原样返回，不白白拷贝一遍
        if (firstZero == 0 && points[0].X == 0)
        {
            return points;
        }

        var result = new CurvePoint[points.Count - firstZero];
        // 首点用**裁完后第一个点**的 Y（它的 X 本来就≈0，这里落到正 0 上）
        result[0] = new CurvePoint(0, points[firstZero].Y);
        for (var i = 1; i < result.Length; i++)
        {
            result[i] = points[firstZero + i];
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
