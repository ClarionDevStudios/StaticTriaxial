namespace ANX.Method.Bending.Tests;

using System.Globalization;
using ANX.Control;

/// <summary>
/// 拟合结果列 → 曲线标注（<see cref="BendingAnnotations"/>）。历史页回放、导出报表、
/// 试验后自动存图三条路都靠它，口径必须一致。
/// </summary>
public sealed class BendingAnnotationsTests
{
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows(
        params (string SampleId, object? K, object? B, object? Origin)[] rows
    ) =>
        [
            .. rows.Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<
                string,
                object?
            >(StringComparer.Ordinal)
            {
                ["SampleID"] = row.SampleId,
                ["TangentK"] = row.K,
                ["TangentB"] = row.B,
                ["OriginCorrection"] = row.Origin,
            }),
        ];

    [Fact]
    public void 有拟合的件能拼出标注()
    {
        var annotation = BendingAnnotations.Find(
            Rows(("S001", 4.0, -0.1, 0.025)),
            "S001",
            startPercent: 20,
            endPercent: 90
        );

        Assert.NotNull(annotation);
        Assert.Equal(4.0, annotation!.TangentK, 6);
        Assert.Equal(-0.1, annotation.TangentB, 6);
        Assert.Equal(0.025, annotation.OriginCorrection, 6);
        Assert.Equal(20, annotation.StartPercent, 6);
        Assert.Equal(90, annotation.EndPercent, 6);
        Assert.True(annotation.HasFit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    public void 没拟合的件回null(object? tangentK)
    {
        // 关掉原点修正 / 判定失败 / 老记录这两列是空：图上只画峰值点
        Assert.Null(BendingAnnotations.Find(Rows(("S001", tangentK, -0.1, 0.025)), "S001"));
    }

    [Fact]
    public void 找不到该试件时回null()
    {
        Assert.Null(BendingAnnotations.Find(Rows(("S001", 4.0, -0.1, 0.025)), "S999"));
    }

    [Fact]
    public void 百分比取参数表的值_取不到或非法就用默认()
    {
        Assert.Equal(
            (20.0, 90.0),
            BendingAnnotations.ReadPercents(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [BendingParameters.OriginCorrectionStartKey] = "20",
                    [BendingParameters.OriginCorrectionEndKey] = "90",
                }
            )
        );

        // 参数表里没有这两项（或值不是正数）→ 回落 10 / 80
        Assert.Equal((10.0, 80.0), BendingAnnotations.ReadPercents(null));
        Assert.Equal(
            (10.0, 80.0),
            BendingAnnotations.ReadPercents(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [BendingParameters.OriginCorrectionStartKey] = "0",
                    [BendingParameters.OriginCorrectionEndKey] = "x",
                }
            )
        );
    }

    [Fact]
    public void 明细行里的列是数字或文本都能认()
    {
        // 落库是 REAL，读回来可能是 double；老数据/别的写法是字符串 —— 两种都要认
        var fromText = BendingAnnotations.Find(Rows(("S001", "4.0", "-0.1", "0.025")), "S001");

        Assert.NotNull(fromText);
        Assert.Equal(
            4.0.ToString(CultureInfo.InvariantCulture),
            fromText!.TangentK.ToString(CultureInfo.InvariantCulture)
        );
    }
}
