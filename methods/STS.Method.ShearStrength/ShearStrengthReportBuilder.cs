namespace STS.Method.ShearStrength;

using System.Globalization;
using System.Text.Json;
using STS.Control;
using STS.Core;
using STS.Core.Reporting;
using STS.Reporting;
using STS.Storage;

/// <summary>一次报表装配的产物：报表文件与曲线图路径。</summary>
public sealed record ShearStrengthReportResult(string ReportPath, IReadOnlyList<string> CurveImagePaths);

/// <summary>
/// T 1236 报表装配：从库里取数 → 每个试件出一张曲线 PNG → 按标签填报表（模板见
/// <see cref="ShearStrengthTemplates"/>）。产物目录沿用现役约定：
/// `&lt;输出根&gt;\抗剪强度\&lt;单号&gt;\实验报告.&lt;语种&gt;.xlsx` 与 `&lt;试件号&gt;.png`。
///
/// 报告内容按 6.1~6.8 立栏：委托 / 样品信息（6.1~6.4）、仪器与成型（6.5~6.6）、
/// 试验结果明细 + 分级平均 + c、φ、r（6.7）、说明与签名（6.8）。
/// </summary>
public sealed class ShearStrengthReportBuilder(
    TestSheetService sheets,
    IReportExporter exporter,
    CurveImageRenderer? curveImages = null
)
{
    /// <summary>输出目录里的方法名（与报表根下的子目录同名）。</summary>
    public const string OutputFolderName = "抗剪强度";

    private static readonly string[] MainColumns =
    [
        "ProjectName",
        "SampleNumber",
        "SampleName",
        "SampleOrigin",
        "SampleSpecification",
        "SampleReceiptDate",
        "SampleDescription",
        "SampleReductionMethod",
        "MachineNameAndID",
        "MoldingMethod",
        "MoldingParameters",
        "TestPerson",
        "CalculatePerson",
        "VerificationPerson",
        "CreatedOn",
        "CohesionKPa",
        "FrictionAngleDeg",
        "EnvelopeCorrelation",
        "EnvelopeReliable",
        "LevelResultsJson",
        "Ssr",
    ];

    private static readonly string[] DetailColumns =
    [
        "SampleID",
        "ConfiningPressure",
        "InitialHeight",
        "InitialDiameter",
        "Mass",
        "WaterContent",
        "DryDensity",
        "WaterContentLoss",
        "MaxAxialLoad",
        "AxialStrain",
        "ShearStrength",
        "StoppedAt15PercentStrain",
    ];

    private readonly CurveImageRenderer _images = curveImages ?? new CurveImageRenderer();

    /// <summary>装配并导出（模板路径由调用方按语种解析好）。</summary>
    public async Task<ShearStrengthReportResult> BuildAsync(
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

        var sheetDirectory = Path.Combine(outputRoot, OutputFolderName, sheetId);
        Directory.CreateDirectory(sheetDirectory);

        var curveImagePaths = new List<string>();
        var detailRows = new List<IReadOnlyDictionary<string, string>>();

        foreach (var row in details)
        {
            var sampleId = Format(row["SampleID"]);

            // 曲线图：一个试件一张（与跑完自动存盘共用同一条渲染路径）
            if (
                await SaveCurveImageAsync(sheetId, sampleId, outputRoot, culture, texts, cancellationToken)
                    .ConfigureAwait(false) is { } imagePath
            )
            {
                curveImagePaths.Add(imagePath);
            }

            detailRows.Add(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Detail.SampleID"] = sampleId,
                    ["Detail.ConfiningPressure"] = Format(row["ConfiningPressure"]),
                    ["Detail.InitialHeight"] = Format(row["InitialHeight"]),
                    ["Detail.InitialDiameter"] = Format(row["InitialDiameter"]),
                    ["Detail.Mass"] = Format(row["Mass"]),
                    ["Detail.WaterContent"] = Format(row["WaterContent"]),
                    ["Detail.DryDensity"] = Format(row["DryDensity"]),
                    ["Detail.WaterContentLoss"] = Format(row["WaterContentLoss"]),
                    ["Detail.PeakLoadN"] = Format(row["MaxAxialLoad"]),
                    ["Detail.AxialStrain"] = Format(row["AxialStrain"]),
                    ["Detail.ShearStrength"] = Format(row["ShearStrength"]),
                    ["Detail.Strain15Text"] = YesNo(row["StoppedAt15PercentStrain"], culture, texts),
                }
            );
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sheet.SheetID"] = sheetId,
            ["Sheet.ProjectName"] = Format(main["ProjectName"]),
            ["Sheet.SampleNumber"] = Format(main["SampleNumber"]),
            ["Sheet.SampleName"] = Format(main["SampleName"]),
            ["Sheet.SampleOrigin"] = Format(main["SampleOrigin"]),
            ["Sheet.SampleSpecification"] = Format(main["SampleSpecification"]),
            ["Sheet.SampleReceiptDate"] = Format(main["SampleReceiptDate"]),
            ["Sheet.SampleDescription"] = Format(main["SampleDescription"]),
            ["Sheet.SampleReductionMethod"] = Format(main["SampleReductionMethod"]),
            ["Sheet.MachineNameAndID"] = Format(main["MachineNameAndID"]),
            ["Sheet.MoldingMethod"] = Format(main["MoldingMethod"]),
            ["Sheet.MoldingParameters"] = Format(main["MoldingParameters"]),
            ["Sheet.TestPerson"] = Format(main["TestPerson"]),
            ["Sheet.CalculatePerson"] = Format(main["CalculatePerson"]),
            ["Sheet.VerificationPerson"] = Format(main["VerificationPerson"]),
            // 试验日期只印日期（与现役一致：库里存的是日期时间文本，陈列裁掉时间）
            ["Sheet.CreatedOnDate"] = DateOnly(main["CreatedOn"]),
            ["Sheet.Date"] = DateOnly(main["CreatedOn"]),
            // 6.8 说明区：留白写区（本期没有单级"说明"字段，先留空）
            ["Sheet.Note"] = string.Empty,
            ["Result.CohesionKPa"] = Format(main["CohesionKPa"]),
            ["Result.FrictionAngleDeg"] = Format(main["FrictionAngleDeg"]),
            ["Result.Correlation"] = Format(main["EnvelopeCorrelation"]),
            ["Result.ReliableText"] = YesNo(main["EnvelopeReliable"], culture, texts),
            ["Result.Ssr"] = Format(main["Ssr"]),
        };

        FillLevelResults(fields, main["LevelResultsJson"]);

        // 文件名带语种：中英两套模板各出一份、互不覆盖
        var reportPath = Path.Combine(sheetDirectory, $"实验报告.{culture}.xlsx");
        await exporter
            .ExportAsync(
                new ReportRequest(
                    "ShearStrength",
                    templatePath,
                    reportPath,
                    fields,
                    detailRows,
                    curveImagePaths
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return new ShearStrengthReportResult(reportPath, curveImagePaths);
    }

    /// <summary>
    /// 把**分级结果 JSON**（`[{sigma3, meanShearStrength, count}, …]`）摊成模板里
    /// 1~3 级的固定标签（`Result.LevelN*`）。级数超出 3 级的先不展示（本期 30 / 60 / 100 三个默认级）。
    /// </summary>
    private static void FillLevelResults(
        IDictionary<string, string> fields,
        object? levelResultsJson
    )
    {
        var json = Format(levelResultsJson);

        List<LevelLine>? levels = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                levels = JsonSerializer.Deserialize<List<LevelLine>>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
            }
            catch (JsonException)
            {
                levels = null; // 老记录 / 坏 JSON：留空，不把报表导不出去
            }
        }

        for (var i = 1; i <= 3; i++)
        {
            var level = levels is not null && levels.Count >= i ? levels[i - 1] : null;

            fields[$"Result.Level{i}Sigma3"] = level is null ? string.Empty : Number(level.Sigma3);
            fields[$"Result.Level{i}Mean"] = level is null
                ? string.Empty
                : Number(level.MeanShearStrength);
            fields[$"Result.Level{i}Count"] = level is null
                ? string.Empty
                : Number(level.Count);
        }
    }

    /// <summary>
    /// 把**某一件试件**的曲线图写成 PNG（不生成整份报表）。
    /// 两处共用这一份实现：导出报表时逐件出图；**试验完成后自动存盘**（执行页调）。
    /// 同名即覆盖 —— 重跑同一件以最后一次为准（报表整单一次成型，曲线图逐件刷新）。
    /// </summary>
    /// <returns>图片路径；这件没有曲线数据（空件 / 未接触）时返回 <c>null</c>，不写空图。</returns>
    public Task<string?> SaveCurveImageAsync(
        string sheetId,
        string sampleId,
        string outputRoot,
        string culture = "zh-CN",
        ITextResolver? texts = null,
        CancellationToken cancellationToken = default
    )
    {
        var curve = CurveDisplay.TrimUnzeroedStart(sheets.ReadCurve(sheetId, sampleId));
        if (curve.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        var imagePath = Path.Combine(outputRoot, OutputFolderName, sheetId, $"{sampleId}.png");

        // 图题与轴名也走资源键（按报表语种解析），不在方法里写死中文；
        // T 1236 没有"修正直线"这类标注（那套是弯曲的），图上只标峰值点 —— annotation 传 null。
        _images.SavePng(
            curve,
            imagePath,
            Text(ShearStrengthMetricKeys.CurveTitle, culture, texts, sampleId),
            Text("Chart.Axis.Deformation", culture, texts),
            Text("Chart.Axis.Load", culture, texts),
            annotation: null,
            CurveMarkTexts.From(key => Text(key, culture, texts))
        );

        return Task.FromResult<string?>(imagePath);
    }

    /// <summary>字段值 → 文本（数值按 0.###；空值给空串，模板里就是留白）。</summary>
    private static string Format(object? value) =>
        value switch
        {
            null => string.Empty,
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>报表里的"日期"只印**日期**、不带时间；认不出时间就原样返回。</summary>
    private static string DateOnly(object? value)
    {
        var text = Format(value);

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : text;
    }

    /// <summary>
    /// 0/1 类标记的文案：1 → 「是」、0 → 「否」、空 → 空串（复用界面上的 `Common.Yes/No` 文案）。
    /// </summary>
    private static string YesNo(object? flag, string culture, ITextResolver? texts)
    {
        var text = Format(flag);
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return string.Empty;
        }

        return Math.Abs(value) < 0.0001
            ? Text("Common.No", culture, texts)
            : Text("Common.Yes", culture, texts);
    }

    /// <summary>取译文；没有解析器（无界面 / 测试场景）时退回键名 —— 漏翻可见，不悄悄显示中文。</summary>
    private static string Text(string key, string culture, ITextResolver? texts, params object[] args)
    {
        var format = texts?.Get(key, culture) ?? key;

        return args.Length == 0
            ? format
            : string.Format(CultureInfo.InvariantCulture, format, args);
    }

    /// <summary>分级结果 JSON 的一行（键名与 <c>ShearStrengthExecution.EncodeLevelResults</c> 一致）。</summary>
    private sealed record LevelLine(double Sigma3, double MeanShearStrength, int Count);
}
