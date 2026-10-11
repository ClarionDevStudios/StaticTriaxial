namespace ANX.Method.Bending;

using FlexCel.XlsAdapter;

/// <summary>
/// T 0715 的**出厂报表模板**（标签制，中英两套）。首版只做"外观与固定文字"，
/// 版式结构固定 —— 与既定决策一致（模板外部化 + 标签写入 + 恢复出厂模板）。
/// 模板不存在时由程序写出，保证任何一台机器上"导出报表"都能用。
/// </summary>
public static class BendingTemplates
{
    /// <summary>模板文件名的语种后缀。</summary>
    public const string ChineseCulture = "zh-CN";

    public const string EnglishCulture = "en-US";

    /// <summary>确保两套模板存在；返回中文模板路径。</summary>
    public static string EnsureDefaultTemplates(string templatesRoot)
    {
        Directory.CreateDirectory(templatesRoot);

        foreach (var culture in new[] { ChineseCulture, EnglishCulture })
        {
            var path = Path.Combine(templatesRoot, $"Bending.{culture}.xlsx");
            if (!File.Exists(path))
            {
                Write(path, culture);
            }
        }

        return Path.Combine(templatesRoot, $"Bending.{ChineseCulture}.xlsx");
    }

    /// <summary>取指定语种的模板路径（不存在则先写出）。</summary>
    public static string Resolve(string templatesRoot, string culture)
    {
        EnsureDefaultTemplates(templatesRoot);
        var normalized = string.Equals(culture, EnglishCulture, StringComparison.OrdinalIgnoreCase)
            ? EnglishCulture
            : ChineseCulture;
        return Path.Combine(templatesRoot, $"Bending.{normalized}.xlsx");
    }

    private static void Write(string path, string culture)
    {
        var isEnglish = string.Equals(culture, EnglishCulture, StringComparison.OrdinalIgnoreCase);

        string T(string zh, string en) => isEnglish ? en : zh;

        var workbook = new XlsFile();
        workbook.NewFile();

        // 抬头（Sheet./Param./Result./Device. 前缀，与 ReportFieldCatalog 的约定一致）
        workbook.SetCellValue(1, 1, T("沥青混合料弯曲试验报告", "Asphalt Mixture Bending Test Report"));
        workbook.SetCellValue(2, 1, T("试验单号", "Sheet ID"));
        workbook.SetCellValue(2, 2, "{{Sheet.SheetID}}");
        workbook.SetCellValue(3, 1, T("项目名称", "Project"));
        workbook.SetCellValue(3, 2, "{{Sheet.ProjectName}}");
        workbook.SetCellValue(4, 1, T("试验目的", "Objective"));
        workbook.SetCellValue(4, 2, "{{Sheet.TestObjective}}");
        workbook.SetCellValue(5, 1, T("试验温度(℃)", "Temperature (C)"));
        workbook.SetCellValue(5, 2, "{{Param.TestTemperature}}");
        workbook.SetCellValue(6, 1, T("加载速率(mm/min)", "Loading rate (mm/min)"));
        workbook.SetCellValue(6, 2, "{{Param.LoadRate}}");
        workbook.SetCellValue(7, 1, T("跨径(mm)", "Span (mm)"));
        workbook.SetCellValue(7, 2, "{{Param.SpanLength}}");
        workbook.SetCellValue(8, 1, T("试验日期", "Test date"));
        workbook.SetCellValue(8, 2, "{{Device.TestDate}}");
        workbook.SetCellValue(9, 1, T("有效试件数", "Effective specimens"));
        workbook.SetCellValue(9, 2, "{{Result.EffectiveCount}}");
        workbook.SetCellValue(10, 1, T("弯拉强度平均值(MPa)", "Mean flexural strength (MPa)"));
        workbook.SetCellValue(10, 2, "{{Result.FlexuralStrengthAvg}}");
        workbook.SetCellValue(11, 1, T("最大弯拉应变平均值(με)", "Mean max strain (ue)"));
        workbook.SetCellValue(11, 2, "{{Result.MaxBendingStrainAvg}}");

        // 明细标题行
        var headerRow = 13;
        workbook.SetCellValue(headerRow, 1, T("试件号", "Sample"));
        workbook.SetCellValue(headerRow, 2, T("宽度(mm)", "Width"));
        workbook.SetCellValue(headerRow, 3, T("高度(mm)", "Height"));
        workbook.SetCellValue(headerRow, 4, T("峰值荷载(kN)", "Peak load"));
        workbook.SetCellValue(headerRow, 5, T("跨中挠度(mm)", "Deflection"));
        workbook.SetCellValue(headerRow, 6, T("弯拉强度(MPa)", "Strength"));
        workbook.SetCellValue(headerRow, 7, T("最大弯拉应变(με)", "Max strain"));

        // 明细模板行（第一个含 {{Detail.*}} 的行，按行展开）
        var detailRow = headerRow + 1;
        workbook.SetCellValue(detailRow, 1, "{{Detail.SampleID}}");
        workbook.SetCellValue(detailRow, 2, "{{Detail.SampleWidth}}");
        workbook.SetCellValue(detailRow, 3, "{{Detail.SampleHeight}}");
        workbook.SetCellValue(detailRow, 4, "{{Detail.PeakLoadValue}}");
        workbook.SetCellValue(detailRow, 5, "{{Detail.CorrectedDeflection}}");
        workbook.SetCellValue(detailRow, 6, "{{Detail.FlexuralStrength}}");
        workbook.SetCellValue(detailRow, 7, "{{Detail.MaxBendingTensileStrain}}");

        workbook.SetCellValue(detailRow + 2, 1, T("执行标准：JTG 3410-2025 T 0715-2025", "Standard: JTG 3410-2025 T 0715-2025"));

        workbook.Save(path);
    }
}
