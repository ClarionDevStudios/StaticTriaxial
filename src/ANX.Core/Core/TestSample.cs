namespace ANX.Core;

/// <summary>
/// 一个控制周期收到的实时采样快照。单位跟随工地习惯：荷载 kN、位移 mm、时间 s。
/// </summary>
public readonly record struct TestSample(
    double Load,
    double Displacement,
    double ElapsedSeconds
);
