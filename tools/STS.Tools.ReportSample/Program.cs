namespace STS.Tools.ReportSample;

using STS.Core.Reporting;
using STS.Method.ShearStrength;
using STS.Reporting;
using STS.Storage;

/// <summary>
/// 生成一份**样例报表**（中英两套）供版式过目：临时库 → 造一单"3 级 × 3 件已完成" →
/// 走与软件同一条导出链路（标签制模板 + 曲线图）。
///
/// 用法：`dotnet run --project tools\STS.Tools.ReportSample -- &lt;输出目录&gt;`（默认 `样例报表`）。
/// 输出：`&lt;输出目录&gt;\Template\ShearStrength.zh-CN.xlsx|en-US.xlsx`（模板）
/// 与 `&lt;输出目录&gt;\抗剪强度\&lt;单号&gt;\实验报告.zh-CN.xlsx|en-US.xlsx`（报表）+ 曲线图 PNG。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var outputRoot = Path.GetFullPath(args.Length > 0 ? args[0] : "样例报表");
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"sts-report-sample-{Guid.NewGuid():N}.db"
        );

        try
        {
            var module = new ShearStrengthPersistenceModule();
            var initializer = new DatabaseInitializer(new StorageOptions(databasePath), [module]);
            initializer.Initialize();

            var sheets = new TestSheetService(
                initializer,
                new SheetNumberGenerator(initializer),
                [module]
            );

            var sheetId = await sheets.CreateAsync(
                ShearStrengthPersistenceModule.MethodCodeValue,
                "G15 高速 K12+300（样例）",
                "WT"
            );

            await SeedCompletedSheetAsync(sheets, sheetId);

            var exportService = new ShearStrengthReportExportService(
                sheets,
                outputRoot,
                texts: new SampleTexts(),
                parameters: new ShearStrengthParameters()
            );

            var zhPath = await exportService.ExportAsync(sheetId, "zh-CN");
            var enPath = await exportService.ExportAsync(sheetId, "en-US");

            Console.WriteLine($"中文报表：{zhPath}");
            Console.WriteLine($"英文报表：{enPath}");

            // 未替换的标签自检（导出器只填登记过的标签；漏填会以 {{...}} 原样留在表上）
            var leftovers = FindUnreplacedLabels(zhPath);
            Console.WriteLine(
                leftovers.Count == 0
                    ? "标签自检：OK（没有残留的 {{...}}）"
                    : $"标签自检：发现残留 {string.Join("、", leftovers)}"
            );

            // 整表预览 PNG（界面上「打印预览」用的是同一段渲染；不开 Excel 也能先看版式）
            var renderer = new FlexCelReportPreviewRenderer();
            var previewPath = Path.Combine(Path.GetDirectoryName(zhPath)!, "预览.zh-CN.png");
            await File.WriteAllBytesAsync(previewPath, renderer.RenderPng(zhPath));
            Console.WriteLine($"预览图：{previewPath}");

            return 0;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    /// <summary>扫一遍表里所有文本单元格，收集没被替换的 `{{标签}}`（自检用）。</summary>
    private static IReadOnlyList<string> FindUnreplacedLabels(string reportPath)
    {
        var workbook = new FlexCel.XlsAdapter.XlsFile();
        workbook.Open(reportPath);

        var found = new List<string>();

        for (var row = 1; row <= workbook.RowCount; row++)
        {
            for (var col = 1; col <= workbook.ColCount; col++)
            {
                if (workbook.GetCellValue(row, col) is string text)
                {
                    var start = text.IndexOf("{{", StringComparison.Ordinal);
                    if (start >= 0)
                    {
                        var end = text.IndexOf("}}", start + 2, StringComparison.Ordinal);
                        found.Add(end < 0 ? text : text[start..(end + 2)]);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>造一张"已完成"的单：3 级 × 3 件 + 收尾参数（c / φ / r + 分级结果）。</summary>
    private static async Task SeedCompletedSheetAsync(TestSheetService sheets, string sheetId)
    {
        var index = 0;

        foreach (var (sigma3, strength) in new[] { (30.0, 130.0), (60.0, 220.0), (100.0, 340.0) })
        {
            for (var i = 0; i < 3; i++)
            {
                index++;
                var sampleId = $"S{index:D3}";

                await sheets.UpsertSampleInfoAsync(
                    sheetId,
                    sampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["ConfiningPressure"] = sigma3,
                        ["InitialHeight"] = 306,
                        ["InitialDiameter"] = 100,
                        ["Mass"] = 3700 + (index * 3),
                        ["WaterContent"] = 5.2,
                        ["DryDensity"] = 2.21,
                        ["WaterContentLoss"] = 0.3,
                    }
                );

                // 一条"上升—峰值—回落"的样例曲线（形状只供版式与出图预览用）
                var curve = new List<STS.Core.CurvePoint>();
                for (var n = 0; n <= 92; n++)
                {
                    var deformation = n * 0.5;
                    var load = 0.16 * deformation * (deformation < 40 ? 1 : 1 - (deformation - 40) * 0.012);
                    curve.Add(new STS.Core.CurvePoint(deformation, load));
                }

                await sheets.UpdateDetailFieldsAsync(
                    sheetId,
                    sampleId,
                    new Dictionary<string, double>(StringComparer.Ordinal)
                    {
                        ["MaxAxialLoad"] = 3800,
                        ["PeakDeformation"] = 25.4,
                        ["AxialStrain"] = 8.3,
                        ["ShearStrength"] = strength,
                        ["StoppedAt15PercentStrain"] = 0,
                        ["TestStatus"] = 1,
                    },
                    curve
                );
            }
        }

        await sheets.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                // 报告 6.1~6.6 的委托 / 样品 / 仪器 / 成型信息（样例值）
                ["SampleNumber"] = "JC-2026-1016-01",
                ["SampleName"] = "级配碎石",
                ["SampleOrigin"] = "示例料场",
                ["SampleSpecification"] = "0~31.5mm",
                ["SampleReceiptDate"] = "2026-10-15",
                ["SampleDescription"] = "骨架密实型，级配良好，无明显离析（样例）。",
                ["SampleReductionMethod"] = "四分法",
                ["MachineNameAndID"] = "土工三轴仪（GR-1）/ 编号 TGSY-2026-001",
                ["MoldingMethod"] = "按 T 1227 静压法成型",
                ["MoldingParameters"] = "分 3 层击实；目标含水率 5.2%；目标干密度 2.21 g/cm³",
                ["TestPerson"] = "张工",
                ["CalculatePerson"] = "李工",
                ["VerificationPerson"] = "王工",
                ["CohesionKPa"] = 11.547,
                ["FrictionAngleDeg"] = 30.0,
                ["EnvelopeCorrelation"] = 0.998,
                ["EnvelopeReliable"] = 1.0,
                ["LevelResultsJson"] =
                    "[{\"sigma3\":30,\"meanShearStrength\":130,\"count\":3},"
                    + "{\"sigma3\":60,\"meanShearStrength\":220,\"count\":3},"
                    + "{\"sigma3\":100,\"meanShearStrength\":340,\"count\":3}]",
            }
        );
        await sheets.CompleteAsync(sheetId);
    }

    /// <summary>样例工具的最小取词器（把报表用到的几个键给上中英文；其余原样回键名）。</summary>
    private sealed class SampleTexts : ITextResolver
    {
        public string Get(string key, string culture)
        {
            var isEnglish = string.Equals(culture, "en-US", StringComparison.OrdinalIgnoreCase);

            return key switch
            {
                "Method.ShearStrength.CurveTitle" => isEnglish ? "Specimen {0}" : "试件 {0}",
                "Chart.Axis.Deformation" => isEnglish ? "Deformation (mm)" : "变形 (mm)",
                "Chart.Axis.Load" => isEnglish ? "Load (kN)" : "荷载 (kN)",
                "Curve.Mark.Peak" => isEnglish
                    ? "Peak {0:0.00} kN / {1:0.00} mm"
                    : "峰值 {0:0.00} kN / {1:0.00} mm",
                "Curve.Mark.Origin" => isEnglish ? "Origin {0:0.00} mm" : "修正原点 {0:0.00} mm",
                "Curve.Mark.FitBound" => isEnglish ? "{0:0.#}% {1:0.00} kN" : "{0:0.#}% {1:0.00} kN",
                "Common.Yes" => isEnglish ? "Yes" : "是",
                "Common.No" => isEnglish ? "No" : "否",
                _ => key,
            };
        }
    }
}
