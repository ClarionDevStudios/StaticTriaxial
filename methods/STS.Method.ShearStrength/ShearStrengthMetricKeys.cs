namespace STS.Method.ShearStrength;

/// <summary>
/// 实时指标卡 / 判定结果卡的名字资源键（执行页按当前语言取译文；键与
/// <c>ShearStrengthExecution.Metrics</c> 的 Key 一一对应）。
/// 其中 `load` / `displacement` / `elapsed` 是执行页控制循环的**实时刷新口**，
/// `shearStrength` / `axialStrain` 在单件判定落库时填（<c>CompleteSampleAsync</c> 的返回字典）。
/// </summary>
public static class ShearStrengthMetricKeys
{
    public const string Load = "Method.ShearStrength.Load";

    public const string Displacement = "Method.ShearStrength.Displacement";

    public const string ShearStrength = "Method.ShearStrength.ShearStrength";

    public const string AxialStrain = "Method.ShearStrength.AxialStrain";

    public const string Elapsed = "Method.ShearStrength.Elapsed";

    /// <summary>全部键（供语言字典一致性用例把守）。</summary>
    public static readonly string[] All =
    [
        Load,
        Displacement,
        ShearStrength,
        AxialStrain,
        Elapsed,
    ];
}
