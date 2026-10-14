namespace ANX.Method.Bending;

using System.Globalization;
using ANX.Core.Reporting;
using ANX.Reporting;
using ANX.Storage;

/// <summary>一次报表装配的产物：报表文件与曲线图路径。</summary>
public sealed record BendingReportResult(string ReportPath, IReadOnlyList<string> CurveImagePaths);

/// <summary>
/// T0715 报表装配：从库里取数 → 每个试件出一张曲线 PNG → 按标签填报表。
/// 装配放在**方法级**（它才知道自己的字段与标签），框架只提供"读行"与"填标签"两个通用能力。
/// 产物目录沿用现役约定：`&lt;输出根&gt;\弯曲\&lt;单号&gt;\实验报告.xlsx` 与 `&lt;试样号&gt;.png`。
/// </summary>
public sealed class BendingReportBuilder(
    TestSheetService sheets,
    IReportExporter exporter,
    CurveImageRenderer? curveImages = null
)
{
    private static readonly string[] MainColumns =
    [
        "ProjectName",
        "TestObjective",
        "TestTemperature",
        "LoadRate",
        "SpanLength",
        "EffectiveCount",
        "FlexuralStrengthAvg",
        "MaxBendingStrainAvg",
        "CreatedOn",
    ];

    private static readonly string[] DetailColumns =
    [
        "SampleID",
        "SampleWidth",
        "SampleHeight",
        "PeakLoadValue",
        "CorrectedDeflection",
        "FlexuralStrength",
        "MaxBendingTensileStrain",
    ];

    private readonly CurveImageRenderer _images = curveImages ?? new CurveImageRenderer();

    /// <summary>装配并导出。</summary>
    public async Task<BendingReportResult> BuildAsync(
        string sheetId,
        string templatePath,
        string outputRoot,
        string culture = "zh-CN",
        ITextResolver? texts = null,
        CancellationToken cancellationToken = default
    )
    {
        var main = sheets.ReadMainRow(sheetId, MainColumns);
        var details = sheets.ReadDetailRows(sheetId, DetailColumns);

        var sheetDirectory = Path.Combine(outputRoot, "弯曲", sheetId);
        Directory.CreateDirectory(sheetDirectory);

        var curveImagePaths = new List<string>();
        var detailRows = new List<IReadOnlyDictionary<string, string>>();

        foreach (var row in details)
        {
            var sampleId = Format(row["SampleID"]);

            // 曲线图：一个试件一张（与自动存盘共用 SaveCurveImage，见下）
            if (SaveCurveImage(sheetId, sampleId, outputRoot, culture, texts) is { } imagePath)
            {
                curveImagePaths.Add(imagePath);
            }

            detailRows.Add(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Detail.SampleID"] = sampleId,
                    ["Detail.SampleWidth"] = Format(row["SampleWidth"]),
                    ["Detail.SampleHeight"] = Format(row["SampleHeight"]),
                    ["Detail.PeakLoadValue"] = Format(row["PeakLoadValue"]),
                    ["Detail.CorrectedDeflection"] = Format(row["CorrectedDeflection"]),
                    ["Detail.FlexuralStrength"] = Format(row["FlexuralStrength"]),
                    ["Detail.MaxBendingTensileStrain"] = Format(row["MaxBendingTensileStrain"]),
                }
            );
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sheet.SheetID"] = sheetId,
            ["Sheet.ProjectName"] = Format(main["ProjectName"]),
            ["Sheet.TestObjective"] = Format(main["TestObjective"]),
            ["Param.TestTemperature"] = Format(main["TestTemperature"]),
            ["Param.LoadRate"] = Format(main["LoadRate"]),
            ["Param.SpanLength"] = Format(main["SpanLength"]),
            ["Result.FlexuralStrengthAvg"] = Format(main["FlexuralStrengthAvg"]),
            ["Result.MaxBendingStrainAvg"] = Format(main["MaxBendingStrainAvg"]),
            ["Result.EffectiveCount"] = Format(main["EffectiveCount"]),
            ["Device.TestDate"] = Format(main["CreatedOn"]),
        };

        // 文件名带语种：中英两套模板各出一份、互不覆盖（"已生成报表不追溯"是既定口径）
        var reportPath = Path.Combine(sheetDirectory, $"实验报告.{culture}.xlsx");
        await exporter
            .ExportAsync(
                new ReportRequest("T0715", templatePath, reportPath, fields, detailRows),
                cancellationToken
            )
            .ConfigureAwait(false);

        return new BendingReportResult(reportPath, curveImagePaths);
    }

    private static string Format(object? value) =>
        value switch
        {
            null => string.Empty,
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    /// <summary>
    /// 取译文；没有解析器（无界面 / 测试场景）时退回键名 —— 与 IAP"缺键可见"的口径一致，
    /// 免得漏翻时悄悄显示成中文或空白。
    /// </summary>
    private static string Text(string key, string culture, ITextResolver? texts, params object[] args)
    {
        var format = texts?.Get(key, culture) ?? key;

        return args.Length == 0
            ? format
            : string.Format(CultureInfo.InvariantCulture, format, args);
    }

    /// <summary>
    /// 把**某一件试件**的曲线图写成 PNG（不生成整份报表）。
    ///
    /// 两处共用这一份实现：
    /// <list type="bullet">
    ///   <item>导出报表时逐件出图（<see cref="BuildAsync"/>）；</item>
    ///   <item>**试验完成后自动存盘**（用户 2026-10-13/2026-10-14 的原始口径：曲线图在试验完成后
    ///         自动生成保存到相应路径）—— 由 <see cref="BendingReportExportService.SaveCurveImageAsync"/>
    ///         转给上层。</item>
    /// </list>
    /// 产物与现役一致：`<输出根>\弯曲\<单号>\<试样号>.png`。同名即覆盖 —— 重跑同一件以最后一次为准，
    /// 与"已生成的报表不追溯"是两回事（报表整单一次成型，曲线图逐件刷新）。
    /// </summary>
    /// <returns>图片路径；这件没有曲线数据（空件 / 未接触）时返回 <c>null</c>，不写空图。</returns>
    public string? SaveCurveImage(
        string sheetId,
        string sampleId,
        string outputRoot,
        string culture = "zh-CN",
        ITextResolver? texts = null
    )
    {
        var curve = sheets.ReadCurve(sheetId, sampleId);
        if (curve.Count == 0)
        {
            return null;
        }

        var imagePath = Path.Combine(outputRoot, "弯曲", sheetId, $"{sampleId}.png");

        // 图题与轴名也走资源键（按报表语种解析），不在方法里写死中文
        _images.SavePng(
            curve,
            imagePath,
            Text(BendingMetricKeys.CurveTitle, culture, texts, sampleId),
            Text(BendingCurveAxes.Deflection, culture, texts),
            Text(BendingCurveAxes.Load, culture, texts)
        );

        return imagePath;
    }
}
