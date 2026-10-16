namespace STS.Core;

/// <summary>
/// 曲线上的一个采样点。X = 横轴，Y = 纵轴。
/// 约定：试验曲线一律用"修正后位移(mm) - 荷载(kN)"，与现役的荷载-位移曲线口径一致。
/// </summary>
public readonly record struct CurvePoint(double X, double Y);
