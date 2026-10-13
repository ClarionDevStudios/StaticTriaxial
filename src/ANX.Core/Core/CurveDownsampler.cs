namespace ANX.Core;

using System.Collections.Generic;

/// <summary>
/// 曲线**显示抽稀**：把要画到屏幕上的点数压到"绘图区像素量级"，同时保住曲线形态。
///
/// **为什么需要**：界面曲线每次重画都是**全量**绘制（ScottPlot 是立即模式：轴、刻度、折线一起
/// 光栅化成一帧），点数随试验时长线性增长 —— 长试验（十分钟级）会把 UI 线程吃满。
/// 抽稀**只影响显示**：落库曲线与判定输入仍是全量点（见 <c>TestSequencer.Curve</c>）。
///
/// **做法（MinMax 分桶）**：按数组序号把点等分成 N 个桶，每桶只保留 **Y 最小与 Y 最大** 的两个点，
/// 再按原顺序输出。每个桶的极值都留住了，所以**峰值与下降段不会丢**；首末点强制保留。
/// 采样在时间上均匀（20ms 一拍），X（跨中挠度）近似等距，所以按序号分桶与按 X 分桶效果等价；
/// 本方法只依赖入参顺序，不要求 X 单调。
///
/// **三条硬保证**（不依赖分桶是否走运，单测逐条锁住）：
/// <list type="number">
///   <item><b>峰值必留</b>：Y 最大的那个点一定在结果里（多个并列时留最先出现的）；</item>
///   <item><b>谷值必留</b>：Y 最小的那个点一定在结果里（破坏后的下降段谷底不会被抹平）；</item>
///   <item><b>量程必准</b>：X 的最小/最大点也强制保留 —— 界面是"抽稀后再 AutoScale"，
///         丢掉 X 极值会让坐标轴比真实曲线短一截。</item>
/// </list>
/// </summary>
public static class CurveDownsampler
{
    /// <summary>抽稀后保留点数的下限：再少曲线就会被压成几条直线段。</summary>
    public const int MinimumPoints = 16;

    /// <summary>留给"首末点 + 全局峰/谷 + 全局 X 极值"的保底名额（去重后实际更少）。</summary>
    private const int ReservedPoints = 6;

    /// <summary>
    /// 把 <paramref name="points"/> 抽稀到不超过 <paramref name="maxPoints"/> 个点。
    /// 点数本来就不超过上限时**原样返回同一个列表**（不拷贝、不分配）。
    /// </summary>
    public static IReadOnlyList<CurvePoint> Downsample(
        IReadOnlyList<CurvePoint> points,
        int maxPoints
    )
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoints, MinimumPoints);

        if (points.Count <= maxPoints)
        {
            return points;
        }

        // 每桶最多留 min + max 两个点，所以桶数取"扣掉保底名额后的一半"
        var bucketCount = Math.Max(2, (maxPoints - ReservedPoints) / 2);
        var keep = new SortedSet<int>();

        // 保底 1：首末点（曲线的起止）
        _ = keep.Add(0);
        _ = keep.Add(points.Count - 1);

        // 保底 2/3/4：全局峰值、全局谷值、全局 X 极值（YX 都算，且不依赖 X 是否单调）
        var peakIndex = 0;
        var valleyIndex = 0;
        var minXIndex = 0;
        var maxXIndex = 0;
        for (var i = 1; i < points.Count; i++)
        {
            // 并列时留**最先出现**的那个点：与"首次达到峰值"的直觉一致
            if (points[i].Y > points[peakIndex].Y)
            {
                peakIndex = i;
            }

            if (points[i].Y < points[valleyIndex].Y)
            {
                valleyIndex = i;
            }

            if (points[i].X < points[minXIndex].X)
            {
                minXIndex = i;
            }

            if (points[i].X > points[maxXIndex].X)
            {
                maxXIndex = i;
            }
        }

        _ = keep.Add(peakIndex);
        _ = keep.Add(valleyIndex);
        _ = keep.Add(minXIndex);
        _ = keep.Add(maxXIndex);

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var from = (int)((long)bucket * points.Count / bucketCount);
            var to = (int)((long)(bucket + 1) * points.Count / bucketCount);
            if (to <= from)
            {
                continue;
            }

            var minIndex = from;
            var maxIndex = from;
            for (var i = from + 1; i < to; i++)
            {
                if (points[i].Y < points[minIndex].Y)
                {
                    minIndex = i;
                }

                if (points[i].Y > points[maxIndex].Y)
                {
                    maxIndex = i;
                }
            }

            _ = keep.Add(minIndex);
            _ = keep.Add(maxIndex);
        }

        var result = new CurvePoint[keep.Count];
        var index = 0;
        foreach (var keptIndex in keep)
        {
            result[index++] = points[keptIndex];
        }

        return result;
    }
}
