namespace ANX.Method.Bending;

/// <summary>
/// T 0715 曲线的轴物理量资源键。执行侧与浏览侧各要实现 <c>ICurveAxisSource</c>，
/// 键只在这里声明一份 —— 免得两处各写一遍、改一处漏一处。
/// 资源键本身放在界面库里（`Resources\Languages\*.xaml`），方法工程只持有键名。
/// </summary>
public static class BendingCurveAxes
{
    /// <summary>横轴：跨中挠度（mm）。</summary>
    public const string Deflection = "Chart.Axis.Deflection";

    /// <summary>纵轴：荷载（kN）。</summary>
    public const string Load = "Chart.Axis.Load";
}
