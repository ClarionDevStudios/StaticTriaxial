namespace STS.Control;

using System.Globalization;
using STS.Core;

/// <summary>图上的一条标注要"画什么"（不管用什么库画）。</summary>
public enum CurveMarkKind
{
    /// <summary>峰值点（实心圆）。</summary>
    Peak,

    /// <summary>修正直线（虚线，从修正原点横跨到曲线最右）。</summary>
    TangentLine,

    /// <summary>修正原点（空心圆，落在 X 轴上）。</summary>
    Origin,

    /// <summary>拟合区间两端（空心圆，落在曲线上）。</summary>
    FitBound,
}

/// <summary>
/// 一条标注：线的两端（点标注则 X1/Y1 = 位置）+ 文字。
/// </summary>
public sealed record CurveMark(
    CurveMarkKind Kind,
    double X1,
    double Y1,
    double X2,
    double Y2,
    string Label
);

/// <summary>
/// 标注文案的三个模板（<c>{0}</c> 之类的占位符，各条的占位符含义见 <see cref="CurveAnnotations.Build"/>）。
/// **由调用方给**：界面按界面语言、报表按报表语种，所以标注文字在存盘图里也是对应语言。
/// </summary>
public sealed record CurveMarkTexts(string PeakFormat, string OriginFormat, string BoundFormat)
{
    /// <summary>
    /// 中文兜底（与界面 2026-10-13 起用的写法**逐字相同**）：取不到译文时用它，
    /// 保证"资源里漏了一键"不会让图上的标注变成空白或键名。
    /// </summary>
    public static CurveMarkTexts Default { get; } =
        new("峰值 {0:0.00} kN / {1:0.00} mm", "修正原点 {0:0.00} mm", "{0:0.#}% {1:0.00} kN");

    /// <summary>按给定的取词函数拼一套（界面传界面的取词、报表传"按语种取词"的那一个）。</summary>
    public static CurveMarkTexts From(Func<string, string> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);

        return new CurveMarkTexts(
            resolve(CurveMarkKeys.Peak),
            resolve(CurveMarkKeys.Origin),
            resolve(CurveMarkKeys.FitBound)
        );
    }
}

/// <summary>
/// 曲线标注三个文案的资源键。放在 <c>STS.Control</c>（而不是方法工程）是因为**界面与报表共用同一组键**：
/// 界面取界面语言、报表按语种取，两处文案不会分叉。
/// </summary>
public static class CurveMarkKeys
{
    /// <summary>峰值点（<c>{0}</c>=荷载 kN、<c>{1}</c>=位移 mm）。</summary>
    public const string Peak = "Curve.Mark.Peak";

    /// <summary>修正原点（<c>{0}</c>=位移 mm）。</summary>
    public const string Origin = "Curve.Mark.Origin";

    /// <summary>拟合区间两端（<c>{0}</c>=峰值荷载的百分数、<c>{1}</c>=该处荷载 kN）。</summary>
    public const string FitBound = "Curve.Mark.FitBound";

    /// <summary>全部键 —— 供"语言字典里是否有这些键"的一致性检查使用。</summary>
    public static readonly string[] All = [Peak, Origin, FitBound];
}

/// <summary>
/// 把 <see cref="CurveAnnotation"/> 算成**一组标注**（纯函数，只描述几何与文案）。
///
/// **为什么抽到这里**（用户 2026-10-15 报的缺陷）：试验完成后**实时曲线**画了修正直线 / 修正原点 /
/// 拟合区间两端，而**存盘的 PNG 没有** —— 两处各画各的，分叉了。现在几何与文案只有这一份：
/// 实时曲线（`STS.Ui.Charts.CurveAnnotator`）与存盘 PNG（`STS.Reporting.CurveImageRenderer`）
/// 都照它画，画出来必然一致。
///
/// 各条标注的文案模板参数：
/// <list type="bullet">
///   <item>峰值点 → <c>{0}</c> = 荷载（kN）、<c>{1}</c> = 位移（mm）；</item>
///   <item>修正原点 → <c>{0}</c> = 修正位移（mm）；</item>
///   <item>拟合区间两端 → <c>{0}</c> = 峰值荷载的百分数、<c>{1}</c> = 该处荷载（kN）；</item>
///   <item>修正直线 → 无文案（只有几何）。</item>
/// </list>
/// </summary>
public static class CurveAnnotations
{
    /// <summary>
    /// 生成标注组。峰值点总要画；<paramref name="annotation"/> 为 null 或没拟合（关掉了原点修正）
    /// 时只有峰值点（与实时曲线同口径）。
    /// </summary>
    public static IReadOnlyList<CurveMark> Build(
        IReadOnlyList<CurvePoint> points,
        CurveAnnotation? annotation,
        CurveMarkTexts? texts = null
    )
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return [];
        }

        var format = texts ?? CurveMarkTexts.Default;
        var marks = new List<CurveMark>(5);

        // ① 峰值点：曲线里 Y（荷载）最大的那个点
        var peak = points.MaxBy(point => point.Y);
        marks.Add(
            new CurveMark(
                CurveMarkKind.Peak,
                peak.X,
                peak.Y,
                peak.X,
                peak.Y,
                Format(format.PeakFormat, peak.Y, peak.X)
            )
        );

        if (annotation is not { HasFit: true } fit)
        {
            return marks;
        }

        // ② 修正直线：横跨整条曲线（右端取曲线最大 X），下端与 X 轴相交于修正原点
        var maxX = points.Max(point => point.X);
        marks.Add(
            new CurveMark(
                CurveMarkKind.TangentLine,
                fit.OriginCorrection,
                0,
                maxX,
                (fit.TangentK * maxX) + fit.TangentB,
                string.Empty
            )
        );

        // ③ 修正原点（直线与 X 轴交点）坐标值
        marks.Add(
            new CurveMark(
                CurveMarkKind.Origin,
                fit.OriginCorrection,
                0,
                fit.OriginCorrection,
                0,
                Format(format.OriginFormat, fit.OriginCorrection)
            )
        );

        // ④ 拟合区间两端：峰值荷载 × 起/止百分比 —— 标在**曲线上**（用户 2026-10-13 更正：
        //    这两点是拟合区间的上下沿，落在实测曲线上，不是落在修正直线上），标注该处荷载值。
        //    曲线上升段没经过该荷载（异常数据）才回落到修正直线上求交点，免得标记掉到图外。
        foreach (var percent in new[] { fit.StartPercent, fit.EndPercent })
        {
            var load = peak.Y * percent / 100.0;
            var point =
                CurveDisplay.PointAtLoad(points, load) ?? new CurvePoint(fit.XAt(load), load);
            marks.Add(
                new CurveMark(
                    CurveMarkKind.FitBound,
                    point.X,
                    point.Y,
                    point.X,
                    point.Y,
                    Format(format.BoundFormat, percent, load)
                )
            );
        }

        return marks;
    }

    /// <summary>
    /// 套模板。**用不变文化**：标注里的数字不该随机器区域设置变（`1.50` 不会在别的区域变成 `1,50`）。
    /// 模板为空（资源里真的给了空串）时回空串，宁可少一行字也别抛异常打断出图。
    /// </summary>
    private static string Format(string template, params object[] args) =>
        string.IsNullOrEmpty(template)
            ? string.Empty
            : string.Format(CultureInfo.InvariantCulture, template, args);
}
