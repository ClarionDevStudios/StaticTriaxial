namespace STS.Control;

/// <summary>
/// 曲线图上"试样完成后"要标注的内容（用户 2026-10-13 定的四条）：
/// <list type="number">
///   <item>峰值点（曲线里荷载最大的那个点）；</item>
///   <item><b>修正直线</b> y = k·x + b，横跨整条曲线、下端与 X 轴相交；</item>
///   <item>该交点即<b>修正原点</b>（判定器口径 <c>max(0, −b/k)</c>），标注坐标值；</item>
///   <item>拟合区间两端（<b>峰值荷载 × 起/止百分比</b>）与修正直线的交点，标注该处荷载值。</item>
/// </list>
/// 参数表关掉「启用曲线原点修正」时判定器不拟合 → <see cref="TangentK"/> = 0 →
/// <see cref="HasFit"/> 为 false，图里就不画直线与交点（用户 2026-10-13 第 4 条）。
///
/// 放在 <c>STS.Control</c>（而不是 <c>STS.Ui</c>）是因为**两个页面共用同一份数据来源**：
/// 试验执行页从本次判定结果拿（<c>ITestExecution</c>），历史数据页从库里已存的
/// <c>TangentK</c>/<c>TangentB</c>/<c>OriginCorrection</c> 拿（<see cref="ITestSheetBrowser"/>）。
/// 两处画出来的标注因此必然一致。
/// </summary>
/// <param name="TangentK">修正直线斜率 k。0 = 没有拟合（关掉了原点修正或拟合失败）。</param>
/// <param name="TangentB">修正直线截距 b。</param>
/// <param name="OriginCorrection">修正原点（直线与 X 轴交点）的 X 值，mm。</param>
/// <param name="StartPercent">拟合区间起点（峰值荷载的百分数，参数表 OriginCorrectionStart）。</param>
/// <param name="EndPercent">拟合区间终点（参数表 OriginCorrectionEnd）。</param>
public sealed record CurveAnnotation(
    double TangentK,
    double TangentB,
    double OriginCorrection,
    double StartPercent = CurveAnnotation.DefaultStartPercent,
    double EndPercent = CurveAnnotation.DefaultEndPercent
)
{
    /// <summary>判定器给的两个默认百分比（与判定器的回落值同口径）。</summary>
    public const double DefaultStartPercent = 10;

    public const double DefaultEndPercent = 80;

    /// <summary>有没有可画的修正直线：k &gt; 0 才说明判定器真的拟合过（上升段斜率必为正）。</summary>
    public bool HasFit => TangentK > 0;

    /// <summary>修正直线上荷载为 <paramref name="loadKN"/> 那一点的 X：x = (y − b) / k。</summary>
    public double XAt(double loadKN) => (loadKN - TangentB) / TangentK;
}
