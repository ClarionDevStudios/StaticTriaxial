namespace ANX.Core;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// 曲线的紧凑 JSON 编解码：`[[x,y],[x,y],…]`。
/// 曲线整条序列化进明细行（既定决策）：只用于回放与画图，不做点级查询。
/// 20ms 采样 × 10 分钟 ≈ 3 万点，进点表会把明细表推到百万行级。
/// </summary>
/// <remarks>
/// **精度取 3 位小数**：X（位移）0.001mm、Y（荷载）0.001kN。
/// 位移 0.001mm 已远超传感器与机械分辨率，荷载 0.001kN 也远超控制器按 10N 上报的分辨率；
/// 再多的位数只是把文件撑大。**上报值另按规范精度**（挠度 0.1mm、强度 0.01MPa、应变 0.1/1με），
/// 两者互不影响。
/// </remarks>
public static class CurveJson
{
    /// <summary>序列化。空曲线得到 `[]`。</summary>
    public static string Encode(IReadOnlyList<CurvePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var builder = new StringBuilder("[");
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append('[')
                .Append(Format(points[i].X))
                .Append(',')
                .Append(Format(points[i].Y))
                .Append(']');
        }

        return builder.Append(']').ToString();
    }

    /// <summary>反序列化。空串 / null / `[]` 都得到空曲线。</summary>
    public static IReadOnlyList<CurvePoint> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var result = new List<CurvePoint>(document.RootElement.GetArrayLength());

        foreach (var pair in document.RootElement.EnumerateArray())
        {
            result.Add(new CurvePoint(pair[0].GetDouble(), pair[1].GetDouble()));
        }

        return result;
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
