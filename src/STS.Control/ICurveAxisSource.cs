namespace STS.Control;

/// <summary>
/// 曲线坐标轴的声明：坐标轴画的是**哪个物理量**（给资源键，界面据此取当前语言的轴名）。
/// 轴名属于方法（各方法自定：如变形 / 荷载），所以由方法声明、框架不管。
/// </summary>
public interface ICurveAxisSource
{
    /// <summary>横轴物理量的资源键（例：Chart.Axis.Deflection）。</summary>
    string CurveXAxisKey { get; }

    /// <summary>纵轴物理量的资源键（例：Chart.Axis.Load）。</summary>
    string CurveYAxisKey { get; }
}
