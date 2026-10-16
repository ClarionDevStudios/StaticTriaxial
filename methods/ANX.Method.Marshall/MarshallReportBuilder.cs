namespace ANX.Method.Marshall;

using System.Globalization;
using ANX.Control;
using ANX.Core;
using ANX.Core.Reporting;
using ANX.Reporting;
using ANX.Storage;

/// <summary>一次报表装配的产物：报表文件与曲线图路径。</summary>
public sealed record MarshallReportResult(string ReportPath, IReadOnlyList<string> CurveImagePaths);

/// <summary>
/// T0709 报表装配：从库里取数 → 每个试件出一张曲线 PNG → 按标签填报表。
/// 版式照现役 `UT0709Report.pas` 的口径（试件区 12 行、汇总三组、稳定度印**实测值 MSRaw**、
/// 「试验类型」按 试验方法×组别 决定、异常值写「合格/剔除」）；
/// 产物目录与命名按本项目既定口径：`&lt;输出根&gt;\稳定度\&lt;单号&gt;\实验报告.&lt;语种&gt;.xlsx`（中英两套）。
/// </summary>
public sealed class MarshallReportBuilder(
    TestSheetService sheets,
    IReportExporter exporter,
    CurveImageRenderer? curveImages = null,
    ITestParameters? parameters = null
)
{
    private static readonly string[] MainColumns =
    [
        "ProjectName",
        "ClientUnits",
        "AsphaltMixtureType",
        "TestStandardsID",
        "TestStandardsDesc",
        "SampleSizeDesc",
        "TestTemperatureDesc",
        "AcceptanceDate",
        "SampleDensity",
        "VoidRatio",
        "AsphaltUsage",
        "ContentByVolume",
        "AsphaltSaturation",
        "MineralAggregateVoidRatio",
        "Tester",
        "CreatedOn",
        "Group1AvgStability",
        "Group1Stability_Std_Dev",
        "Group1Stability_Cv_Percent",
        "Group1AvgFlowValue",
        "Group1Flow_Std_Dev",
        "Group1Flow_Cv_Percent",
        "Group2AvgStability",
        "Group2Stability_Std_Dev",
        "Group2Stability_Cv_Percent",
        "Group2AvgFlowValue",
        "Group2Flow_Std_Dev",
        "Group2Flow_Cv_Percent",
        "StabilityRatio",
    ];

    private static readonly string[] DetailColumns =
    [
        "SampleID",
        "GroupID",
        "SampleInitialDiameter",
        "SampleInitialHeight",
        "MSRaw",
        "FlowValue",
        "MarshallModulus",
        "Effective",
    ];

    private readonly CurveImageRenderer _images = curveImages ?? new CurveImageRenderer();

    /// <summary>装配并导出。</summary>
    public async Task<MarshallReportResult> BuildAsync(
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
        var standardsId = ReadStandardsId(main["TestStandardsID"]);

        var curveImagePaths = new List<string>();
        var detailRows = new List<Dictionary<string, string>>();

        foreach (var row in details)
        {
            var sampleId = Format(row["SampleID"]);

            if (await SaveCurveImageAsync(
                sheetId,
                sampleId,
                outputRoot,
                culture,
                texts,
                cancellationToken
            ) is { } imagePath)
            {
                curveImagePaths.Add(imagePath);
            }

            detailRows.Add(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Detail.SampleId"] = sampleId,
                    ["Detail.SampleDiameter"] = Format(row["SampleInitialDiameter"]),
                    ["Detail.SampleHeight"] = Format(row["SampleInitialHeight"]),
                    ["Detail.TestType"] = TestTypeText(standardsId, ReadGroupId(row["GroupID"]), culture, texts),
                    // 照现役：稳定度一列印**实测值 MSRaw**（芯样同样）。
                    // 这三个量按用户 2026-10-16 定的口径显示**两位小数**（取值 / 存储 / 显示一致）；
                    // 现役报表也是 SetDbl(…, 2)，一致。
                    ["Detail.Stability"] = Format(row["MSRaw"], "0.00"),
                    ["Detail.FlowValue"] = Format(row["FlowValue"], "0.00"),
                    ["Detail.MarshallModulus"] = Format(row["MarshallModulus"], "0.00"),
                    ["Detail.EffectiveText"] = EffectiveText(row["Effective"], culture, texts),
                    ["Detail.Remark"] = string.Empty,
                }
            );
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sheet.SheetID"] = sheetId,
            ["Sheet.ProjectName"] = Format(main["ProjectName"]),
            ["Sheet.ClientUnits"] = Format(main["ClientUnits"]),
            ["Sheet.MixtureType"] = Format(main["AsphaltMixtureType"]),
            ["Sheet.TestMethod"] = Format(main["TestStandardsDesc"]),
            ["Sheet.AcceptanceDate"] = Format(main["AcceptanceDate"]),
            // 试验日期只印日期（用户 2026-10-15 的定例）：库里 CreatedOn 是 `yyyy-MM-dd HH:mm:ss`
            ["Sheet.TestDate"] = DateOnly(main["CreatedOn"]),
            ["Sheet.SampleDensity"] = Format(main["SampleDensity"]),
            ["Sheet.VoidRatio"] = Format(main["VoidRatio"]),
            ["Sheet.AsphaltUsage"] = Format(main["AsphaltUsage"]),
            ["Sheet.ContentByVolume"] = Format(main["ContentByVolume"]),
            ["Sheet.AsphaltSaturation"] = Format(main["AsphaltSaturation"]),
            ["Sheet.MineralAggregateVoidRatio"] = Format(main["MineralAggregateVoidRatio"]),
            ["Sheet.TestTemperature"] = Format(main["TestTemperatureDesc"]),
            ["Sheet.SampleSizeDesc"] = Format(main["SampleSizeDesc"]),
            ["Sheet.Tester"] = Format(main["Tester"]),
            // 汇总区一律两位小数（用户 2026-10-16：马歇尔的这些量比现役多一位）
            ["Result.Group1AvgStability"] = Format(main["Group1AvgStability"], "0.00"),
            ["Result.Group1StabilityStdDev"] = Format(main["Group1Stability_Std_Dev"], "0.00"),
            ["Result.Group1StabilityCvPercent"] = Format(main["Group1Stability_Cv_Percent"], "0.00"),
            ["Result.Group1AvgFlowValue"] = Format(main["Group1AvgFlowValue"], "0.00"),
            ["Result.Group1FlowStdDev"] = Format(main["Group1Flow_Std_Dev"], "0.00"),
            ["Result.Group1FlowCvPercent"] = Format(main["Group1Flow_Cv_Percent"], "0.00"),
            ["Result.Group2AvgStability"] = Format(main["Group2AvgStability"], "0.00"),
            ["Result.Group2StabilityStdDev"] = Format(main["Group2Stability_Std_Dev"], "0.00"),
            ["Result.Group2StabilityCvPercent"] = Format(main["Group2Stability_Cv_Percent"], "0.00"),
            ["Result.Group2AvgFlowValue"] = Format(main["Group2AvgFlowValue"], "0.00"),
            ["Result.Group2FlowStdDev"] = Format(main["Group2Flow_Std_Dev"], "0.00"),
            ["Result.Group2FlowCvPercent"] = Format(main["Group2Flow_Cv_Percent"], "0.00"),
            // 两个比值键**始终要给**：导出器对"没给值的标签"是原样保留（便于发现漏填），
            // 而"本单不做浸水/真空饱水"是**正常的不适用**，所以这里填空串把它印成空格子
            //（现役同样只填对应组那一列，另一列留空）。
            ["Result.ImmersionRatio"] = string.Empty,
            ["Result.VacuumRatio"] = string.Empty,
        };

        // 稳定度比按「本单是哪一种马歇尔」落在对应那一列（现役：H27 = 浸水、I28 = 真空饱水）
        var ratio = Format(main["StabilityRatio"], "0.00");
        if (standardsId == 1)
        {
            fields["Result.ImmersionRatio"] = ratio;
        }
        else if (standardsId == 2)
        {
            fields["Result.VacuumRatio"] = ratio;
        }

        var sheetDirectory = Path.Combine(outputRoot, "稳定度", sheetId);
        Directory.CreateDirectory(sheetDirectory);

        // 文件名带语种：中英两套模板各出一份、互不覆盖（"已生成报表不追溯"是既定口径）
        var reportPath = Path.Combine(sheetDirectory, $"实验报告.{culture}.xlsx");
        await exporter
            .ExportAsync(
                new ReportRequest(
                    "T0709",
                    templatePath,
                    reportPath,
                    fields,
                    detailRows,
                    curveImagePaths
                ),
                cancellationToken
            )
            .ConfigureAwait(false);

        return new MarshallReportResult(reportPath, curveImagePaths);
    }

    /// <summary>
    /// 把**某一件试件**的曲线图写成 PNG（不生成整份报表）—— 报表导出与"试验完成后自动存盘"共用。
    /// 产物：`&lt;输出根&gt;\稳定度\&lt;单号&gt;\&lt;试件号&gt;.png`，同名即覆盖。
    /// </summary>
    public async Task<string?> SaveCurveImageAsync(
        string sheetId,
        string sampleId,
        string outputRoot,
        string culture = "zh-CN",
        ITextResolver? texts = null,
        CancellationToken cancellationToken = default
    )
    {
        // 与历史页回放同一口径：开头的"未归零点"裁掉再画
        var curve = CurveDisplay.TrimUnzeroedStart(sheets.ReadCurve(sheetId, sampleId));
        if (curve.Count == 0)
        {
            return null;
        }

        var imagePath = Path.Combine(outputRoot, "稳定度", sheetId, $"{sampleId}.png");

        var annotation = await ReadAnnotationAsync(sheetId, sampleId, cancellationToken)
            .ConfigureAwait(false);

        _images.SavePng(
            curve,
            imagePath,
            Text(MarshallMetricKeys.CurveTitle, culture, texts, sampleId),
            Text(MarshallCurveAxes.Deformation, culture, texts),
            Text(MarshallCurveAxes.Load, culture, texts),
            annotation,
            CurveMarkTexts.From(key => Text(key, culture, texts))
        );

        return imagePath;
    }

    /// <summary>读某一件的曲线标注（与历史数据页回放同一份解析：<see cref="MarshallAnnotations"/>）。</summary>
    private async Task<CurveAnnotation?> ReadAnnotationAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken
    )
    {
        IReadOnlyDictionary<string, string>? values = parameters is null
            ? null
            : await parameters.LoadAsync(cancellationToken).ConfigureAwait(false);

        var (startPercent, endPercent) = MarshallAnnotations.ReadPercents(values);

        return MarshallAnnotations.Find(
            sheets.ReadDetailRows(sheetId, MarshallAnnotations.Columns),
            sampleId,
            startPercent,
            endPercent
        );
    }

    /// <summary>试验方法代号（0 = 标准、1 = 浸水、2 = 真空饱水；空/异常按标准处理）。</summary>
    private static int ReadStandardsId(object? value) =>
        int.TryParse(Format(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        && parsed is >= 0 and <= 2
            ? parsed
            : 0;

    private static int ReadGroupId(object? value) =>
        int.TryParse(Format(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        && parsed > 0
            ? 1
            : 0;

    /// <summary>
    /// 现役 <c>UT0709Report.pas</c> 的口径：标准组的试验类型都是「标准马歇尔试验」，
    /// 第二组按 试验方法 分成「浸水马歇尔试验 / 真空饱水马歇尔试验」。
    /// </summary>
    private static string TestTypeText(int standardsId, int groupId, string culture, ITextResolver? texts)
    {
        if (groupId == 0)
        {
            return Text(MarshallMetricKeys.TestTypeStandard, culture, texts);
        }

        return standardsId switch
        {
            1 => Text(MarshallMetricKeys.TestTypeImmersion, culture, texts),
            2 => Text(MarshallMetricKeys.TestTypeVacuum, culture, texts),
            _ => Text(MarshallMetricKeys.TestTypeStandard, culture, texts),
        };
    }

    /// <summary>
    /// 「异常值判断」列的文案：照现役 T0709 报表写**合格 / 剔除**（<c>UT0709Report.pas</c>：
    /// <c>IfThen(FIsOutlier, '合格', '剔除')</c>）。<c>Effective</c> 为 0 才算剔除；NULL（只填信息、
    /// 没跑过的件）或 1 都算合格。
    /// </summary>
    private static string EffectiveText(object? effective, string culture, ITextResolver? texts)
    {
        var text = Format(effective);
        var isExcluded = double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value
        ) && Math.Abs(value) < 0.0001;

        return isExcluded
            ? Text(MarshallMetricKeys.ValidityAbnormal, culture, texts)
            : Text(MarshallMetricKeys.ValidityNormal, culture, texts);
    }

    private static string Format(object? value, string format = "0.###") =>
        value switch
        {
            null => string.Empty,
            double number => number.ToString(format, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };

    /// <summary>报表里的"日期"只印日期、不带时间（用户 2026-10-15）。认不出就原样返回，不改坏数据。</summary>
    private static string DateOnly(object? value)
    {
        var text = Format(value);

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : text;
    }

    /// <summary>取译文；没有解析器（无界面 / 测试场景）时退回键名。</summary>
    private static string Text(string key, string culture, ITextResolver? texts, params object[] args)
    {
        var format = texts?.Get(key, culture) ?? key;

        return args.Length == 0
            ? format
            : string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
