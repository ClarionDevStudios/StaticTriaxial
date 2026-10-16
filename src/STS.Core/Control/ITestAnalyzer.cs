namespace STS.Core.Control;

/// <summary>
/// 判定器。要求做成**纯函数**：同样的曲线进、同样的指标出，便于用真机抓帧做黄金用例。
/// 指标以"字段名 → 值"返回，字段名与方法的建表列名一致。
/// </summary>
/// <remarks>
/// 组聚合（一组取平均、超差剔除等）暂不在本契约里，随具体方法一起定型；
/// 这里先只留"单条曲线 → 该试件指标"这一层，避免在没写第一个方法前过度设计。
/// </remarks>
public interface ITestAnalyzer
{
    /// <summary>方法代号，与 <see cref="Methods.ITestMethod.MethodCode"/> 对应。</summary>
    string MethodCode { get; }

    /// <summary>算一条曲线的指标。</summary>
    IReadOnlyDictionary<string, double> AnalyzeSample(IReadOnlyList<CurvePoint> curve);
}
