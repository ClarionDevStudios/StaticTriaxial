namespace ANX.Control;

/// <summary>
/// 曲线坐标轴的声明：坐标轴画的是**哪个物理量**（给资源键，界面据此取当前语言的轴名）。
/// 轴名属于方法（弯曲是"跨中挠度 / 荷载"，马歇尔是"流值 / 荷载"），所以由方法声明、框架不管。
/// </summary>
public interface ICurveAxisSource
{
    /// <summary>横轴物理量的资源键（例：Chart.Axis.Deflection）。</summary>
    string CurveXAxisKey { get; }

    /// <summary>纵轴物理量的资源键（例：Chart.Axis.Load）。</summary>
    string CurveYAxisKey { get; }
}
