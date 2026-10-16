namespace ANX.Method.Marshall;

/// <summary>
/// T 0709 曲线的轴物理量资源键。执行侧与浏览侧各要实现 <c>ICurveAxisSource</c>，
/// 键只在这里声明一份 —— 免得两处各写一遍、改一处漏一处。
/// 资源键本身放在界面库里（`Resources\Languages\*.xaml`），方法工程只持有键名。
/// </summary>
public static class MarshallCurveAxes
{
    /// <summary>横轴：变形（mm）——马歇尔曲线是"变形-荷载"曲线（与弯曲的"跨中挠度"不同一个物理量）。</summary>
    public const string Deformation = "Chart.Axis.Deformation";

    /// <summary>纵轴：荷载（kN）。与弯曲共用同一条键。</summary>
    public const string Load = "Chart.Axis.Load";
}
