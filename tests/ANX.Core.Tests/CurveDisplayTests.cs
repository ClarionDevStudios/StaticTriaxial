namespace ANX.Core.Tests;

using ANX.Core;

/// <summary>
/// 曲线开头修正：老记录开头那几个点是"接触后位移还没归零"时记下的（X 停在旧偏移上，
/// 实测 1.2~10.4mm），调零生效后才从 0.00x 起 —— 这些点在图上是一段折线（用户 2026-10-15）。
/// 口径：**裁掉开头未归零的点 + 首点落在 X = 0**；不动其余点、不做整体平移。
/// </summary>
public sealed class CurveDisplayTests
{
    [Fact]
    public void 裁掉开头未归零的点并让首点落在零点()
    {
        List<CurvePoint> curve =
        [
            new(3.495, 0.051), // 老记录：接触后还没归零，X 停在旧偏移上
            new(3.495, 0.051),
            new(0.005, 0.072), // 从这里开始位移（基本）归零了
            new(0.012, 0.087),
            new(1.250, 9.800),
        ];

        var result = CurveDisplay.TrimUnzeroedStart(curve);

        Assert.Equal(3, result.Count); // 前两点被裁掉
        Assert.Equal(new CurvePoint(0, 0.072), result[0]); // 首点落到 X = 0，Y 取裁完后第一个点的
        Assert.Equal(curve[3], result[1]);
        Assert.Equal(curve[4], result[2]);
    }

    [Fact]
    public void 开头本来就归零_原样返回同一列表()
    {
        List<CurvePoint> curve = [new(0, 0.05), new(0.4, 3.2), new(1.2, 9.0)];

        var result = CurveDisplay.TrimUnzeroedStart(curve);

        Assert.Same(curve, result); // 新记录本来就对：不白白拷贝一遍
    }

    [Fact]
    public void 整条曲线都远离零点_原样返回不动它()
    {
        // 找不到"已归零"的点就当它不是这个问题，**不许把数据裁没**
        List<CurvePoint> curve = [new(5.0, 0.1), new(5.2, 0.4), new(6.0, 9.0)];

        Assert.Same(curve, CurveDisplay.TrimUnzeroedStart(curve));
    }

    [Fact]
    public void 裁完只剩不到两点_原样返回()
    {
        List<CurvePoint> curve = [new(3.0, 0.05), new(0.005, 0.07)];

        Assert.Same(curve, CurveDisplay.TrimUnzeroedStart(curve));
    }

    [Fact]
    public void 空曲线原样返回()
    {
        IReadOnlyList<CurvePoint> curve = [];

        Assert.Same(curve, CurveDisplay.TrimUnzeroedStart(curve));
    }

    [Fact]
    public void 上升段按荷载取点_线性插值()
    {
        // 峰值 5kN 在 (3, 5)：40% = 2kN 落在 (1,1)~(3,5) 之间，t = 0.25 → (1.5, 2)
        List<CurvePoint> curve = [new(0, 0), new(1, 1), new(3, 5), new(6, 3)];

        var point = CurveDisplay.PointAtLoad(curve, 2.0);

        Assert.NotNull(point);
        Assert.Equal(1.5, point!.Value.X, 1e-9);
        Assert.Equal(2.0, point.Value.Y, 1e-9);
    }

    [Fact]
    public void 上升段取点_只看峰值前的上升段()
    {
        // 下降段上也经过 2kN，但拟合区间取的是上升段 → 必须回 (1, 1)~(3, 5) 这一段
        List<CurvePoint> curve = [new(0, 0), new(1, 1), new(3, 5), new(4, 4), new(6, 2)];

        var point = CurveDisplay.PointAtLoad(curve, 2.0);

        Assert.Equal(1.5, point!.Value.X, 1e-9); // 不是下降段的 6mm
    }

    [Fact]
    public void 峰值不到该荷载时回null()
    {
        List<CurvePoint> curve = [new(0, 0), new(1, 3), new(2, 2)];

        Assert.Null(CurveDisplay.PointAtLoad(curve, 5.0));
        Assert.Null(CurveDisplay.PointAtLoad([], 1.0));
    }

    [Fact]
    public void 首点已高于该荷载时回首点()
    {
        List<CurvePoint> curve = [new(0, 4), new(1, 6), new(2, 3)];

        var point = CurveDisplay.PointAtLoad(curve, 2.0);

        Assert.Equal(new CurvePoint(0, 4), point);
    }
}
