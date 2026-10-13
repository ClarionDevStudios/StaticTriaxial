namespace ANX.Core.Tests;

using ANX.Core;

/// <summary>
/// 回放显示修正：老记录的曲线首点是"启动试验时那个位置"（bug 期间的库），
/// 回放时按定义应当是**位移零点**（用户 2026-10-13 发现的问题）。
/// 口径：只动首点、不动其余点、不改库里数据。
/// </summary>
public sealed class CurveDisplayTests
{
    [Fact]
    public void 首点X归零_其余点原样()
    {
        List<CurvePoint> curve =
        [
            new(0.7, 0.05),   // 老记录：首点带着调零前的位移
            new(0.01, 0.30),
            new(1.25, 9.80),
            new(2.40, 6.10),
        ];

        var result = CurveDisplay.ZeroFirstPointX(curve);

        Assert.Equal(4, result.Count);
        Assert.Equal(new CurvePoint(0, 0.05), result[0]); // X 归零，Y（荷载）保留
        Assert.Equal(curve[1], result[1]);
        Assert.Equal(curve[2], result[2]);
        Assert.Equal(curve[3], result[3]);
    }

    [Fact]
    public void 首点已在零点_原样返回同一列表()
    {
        List<CurvePoint> curve = [new(0, 0.05), new(0.4, 3.2)];

        var result = CurveDisplay.ZeroFirstPointX(curve);

        Assert.Same(curve, result); // 新记录本来就对：不白白拷贝一遍
    }

    [Fact]
    public void 空曲线原样返回()
    {
        IReadOnlyList<CurvePoint> curve = [];

        Assert.Same(curve, CurveDisplay.ZeroFirstPointX(curve));
    }
}
