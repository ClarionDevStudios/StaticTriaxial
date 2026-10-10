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

            // 曲线图：一个试件一张，与现役的 Bins\Report\<试验类型>\<单号>\<试样号>.png 一致
            var curve = sheets.ReadCurve($"{sheetId}-{sampleId}");
            if (curve.Count > 0)
            {
                var imagePath = Path.Combine(sheetDirectory, $"{sampleId}.png");
                _images.SavePng(curve, imagePath, $"试件 {sampleId}", "跨中挠度 (mm)", "荷载 (kN)");
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

        var reportPath = Path.Combine(sheetDirectory, "实验报告.xlsx");
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
}
