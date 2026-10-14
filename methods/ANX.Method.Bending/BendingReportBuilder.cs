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
        "ClientUnits",
        "MixtureType",
        "TestObjective",
        "TestTemperature",
        "SampleSizeDesc",
        "LoadRate",
        "SpanLength",
        "AcceptanceDate",
        "SampleDensity",
        "VoidRatio",
        "AsphaltUsage",
        "ContentByVolume",
        "AsphaltSaturation",
        "MineralAggregateVoidRatio",
        "Operator",
        "EffectiveCount",
        "FlexuralStrengthAvg",
        "MaxBendingStrainAvg",
        "CreatedOn",
    ];

    private static readonly string[] DetailColumns =
    [
        "SampleID",
        "SampleLength",
        "SampleWidth",
        "SampleHeight",
        "PeakLoadValue",
        "CorrectedDeflection",
        "FlexuralStrength",
        "MaxBendingTensileStrain",
        "IsEffective",
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
        // 具体类型（不是接口）：下面要给没填长度的行补兜底值，接口索引器是只读的
        var detailRows = new List<Dictionary<string, string>>();

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
                    ["Detail.SampleLength"] = Format(row["SampleLength"]),
                    ["Detail.SampleWidth"] = Format(row["SampleWidth"]),
                    ["Detail.SampleHeight"] = Format(row["SampleHeight"]),
                    ["Detail.PeakLoadValue"] = Format(row["PeakLoadValue"]),
                    ["Detail.CorrectedDeflection"] = Format(row["CorrectedDeflection"]),
                    ["Detail.FlexuralStrength"] = Format(row["FlexuralStrength"]),
                    ["Detail.MaxBendingTensileStrain"] = Format(row["MaxBendingTensileStrain"]),
                    // 异常值判断：现役报表里写「正常」/「异常」（不用 0/1，也不用 InvalidReason）
                    ["Detail.EffectiveText"] = EffectiveText(row["IsEffective"], culture, texts),
                }
            );
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sheet.SheetID"] = sheetId,
            ["Sheet.ProjectName"] = Format(main["ProjectName"]),
            ["Sheet.ClientUnits"] = Format(main["ClientUnits"]),
            ["Sheet.MixtureType"] = Format(main["MixtureType"]),
            ["Sheet.TestObjective"] = Format(main["TestObjective"]),
            ["Sheet.TestTemperature"] = Format(main["TestTemperature"]),
            ["Sheet.SampleSizeDesc"] = Format(main["SampleSizeDesc"]),
            ["Sheet.AcceptanceDate"] = Format(main["AcceptanceDate"]),
            ["Sheet.CreatedOn"] = Format(main["CreatedOn"]),
            ["Sheet.Operator"] = Format(main["Operator"]),
            ["Sheet.SampleDensity"] = Format(main["SampleDensity"]),
            ["Sheet.VoidRatio"] = Format(main["VoidRatio"]),
            ["Sheet.AsphaltUsage"] = Format(main["AsphaltUsage"]),
            ["Sheet.ContentByVolume"] = Format(main["ContentByVolume"]),
            ["Sheet.AsphaltSaturation"] = Format(main["AsphaltSaturation"]),
            ["Sheet.MineralAggregateVoidRatio"] = Format(main["MineralAggregateVoidRatio"]),
            ["Param.TestTemperature"] = Format(main["TestTemperature"]),
            ["Param.LoadRate"] = Format(main["LoadRate"]),
            ["Param.SpanLength"] = Format(main["SpanLength"]),
            ["Result.FlexuralStrengthAvg"] = Format(main["FlexuralStrengthAvg"]),
            ["Result.MaxBendingStrainAvg"] = Format(main["MaxBendingStrainAvg"]),
            ["Result.EffectiveCount"] = Format(main["EffectiveCount"]),
            ["Device.TestDate"] = Format(main["CreatedOn"]),
        };

        // 说明：现役明细第 2 列是「试件长度(mm)」（读明细表 SampleLength）。
        // 本软件的**试样信息填录**目前只有跨径 / 宽 / 高三项（跨径 ≠ 试件长度，规范里是 200 与 250），
        // 所以这里**不做兜底**、该列如实留空 —— 拿跨径去填"长度"列属于编数据。
        // 库里 `SampleLength` 这一列是有的（照现役表结构），要补的话给试样信息加一个字段即可。

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
    /// 「异常值判断」列的文案：照现役报表写**正常 / 异常**（现役 <c>UT0715Report.pas</c>：
    /// <c>if Effective then '正常' else '异常'</c>）。
    /// <c>IsEffective</c> 为 0 才算异常；为 NULL（只填信息、没跑过的件）或 1 都算正常。
    /// </summary>
    private static string EffectiveText(object? isEffective, string culture, ITextResolver? texts)
    {
        var text = Format(isEffective);
        var isExcluded = double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value
        ) && Math.Abs(value) < 0.0001;

        return isExcluded
            ? Text(BendingMetricKeys.ValidityAbnormal, culture, texts)
            : Text(BendingMetricKeys.ValidityNormal, culture, texts);
    }

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
