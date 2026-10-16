namespace ANX.Method.Marshall.Tests;

using ANX.Core;
using ANX.Core.Reporting;
using ANX.Reporting;
using ANX.Storage;
using FlexCel.Core;
using FlexCel.XlsAdapter;
using Microsoft.Data.Sqlite;

/// <summary>
/// 出厂模板（中英两套）与"从历史页导出报表"这条链路：
/// 模板不存在时程序自己写出 → 按语种选模板 → 装配 → 出 Excel。
/// 版式断言逐格照现役 `T0709-2025Template.xlsx` 量出来的数字（行/列/合并/样式/行高）。
/// </summary>
public sealed class MarshallReportTemplateTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-t0709-tpl-{Guid.NewGuid():N}"
    );

    public MarshallReportTemplateTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void 出厂模板应在中英各写一份且幂等()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");

        MarshallTemplates.EnsureDefaultTemplates(templatesRoot);
        Assert.True(File.Exists(Path.Combine(templatesRoot, "Marshall.zh-CN.xlsx")));
        Assert.True(File.Exists(Path.Combine(templatesRoot, "Marshall.en-US.xlsx")));

        // 幂等：再调一次不重写、不报错
        MarshallTemplates.EnsureDefaultTemplates(templatesRoot);

        Assert.Equal(
            Path.Combine(templatesRoot, "Marshall.en-US.xlsx"),
            MarshallTemplates.Resolve(templatesRoot, "en-US")
        );
        // 语种不认识时回落到中文
        Assert.Equal(
            Path.Combine(templatesRoot, "Marshall.zh-CN.xlsx"),
            MarshallTemplates.Resolve(templatesRoot, "ja-JP")
        );
    }

    [Fact]
    public void 模板版式照现役报表()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var path = MarshallTemplates.Resolve(templatesRoot, "zh-CN");

        var zh = new XlsFile();
        zh.Open(path);

        // 标题与报表头（行 2~8）：标签在 A/E/H 列、值在 C/F/I 列
        Assert.Equal("沥青混合料马歇尔稳定度试验报表", zh.GetCellValue(1, 1));
        Assert.Equal("试验项目名称：", zh.GetCellValue(2, 1));
        Assert.Equal("{{Sheet.ProjectName}}", zh.GetCellValue(2, 3));
        Assert.Equal("执行标准：", zh.GetCellValue(2, 5));
        // 规范正式名称是 JTG3410-2025（现役模板里的 JTC 是笔误，用户 2026-10-15 定为改正）
        Assert.Equal("JTG3410-2025 / T0709-2025 沥青混合料马歇尔稳定度试验", zh.GetCellValue(2, 6));
        Assert.Equal("委托单位：", zh.GetCellValue(3, 1));
        Assert.Equal("试验方法：", zh.GetCellValue(3, 5));
        Assert.Equal("沥青混合料类型：", zh.GetCellValue(4, 1));
        Assert.Equal("接样日期：", zh.GetCellValue(4, 5));
        Assert.Equal("试验日期：", zh.GetCellValue(4, 8));
        Assert.Equal("毛体积密度(g/cm³)：", zh.GetCellValue(5, 1));
        Assert.Equal("空隙率(%)：", zh.GetCellValue(5, 5));
        Assert.Equal("沥青用量(%)：", zh.GetCellValue(5, 8));
        Assert.Equal("沥青体积百分率(%)：", zh.GetCellValue(6, 1));
        Assert.Equal("沥青饱和度(%)：", zh.GetCellValue(6, 5));
        Assert.Equal("矿料间隙率(%)：", zh.GetCellValue(6, 8));
        Assert.Equal("试验温度( ℃)：", zh.GetCellValue(7, 1));
        Assert.Equal("试件尺寸：", zh.GetCellValue(7, 5));
        Assert.Equal("仪器设备名称：", zh.GetCellValue(8, 1));
        Assert.Equal("型号：", zh.GetCellValue(8, 5));
        Assert.Equal("设备编号：", zh.GetCellValue(8, 8));

        // 分节标题与明细表头（照现役 R9/R10）
        Assert.Equal("一、试件基本物理指标及试验结果", zh.GetCellValue(9, 1));
        Assert.Equal("试件编号", zh.GetCellValue(10, 1));
        Assert.Equal("试件直径(mm)", zh.GetCellValue(10, 2));
        Assert.Equal("稳定度MS(kN)", zh.GetCellValue(10, 5));
        Assert.Equal("流值FL(mm)", zh.GetCellValue(10, 6));
        Assert.Equal("马歇尔模数T(kN/mm)", zh.GetCellValue(10, 7));
        Assert.Equal("异常值判断", zh.GetCellValue(10, 8));
        Assert.Equal("备注", zh.GetCellValue(10, 9));

        // 明细模板行（第 11 行）放占位符；12~22 行是空的带框行（照现役 12 行明细）
        Assert.Equal("{{Detail.SampleId}}", zh.GetCellValue(11, 1));
        Assert.Equal("{{Detail.EffectiveText}}", zh.GetCellValue(11, 8));
        Assert.Equal("", Convert.ToString(zh.GetCellValue(12, 1)));

        // 汇总区（照现役 R24/R25/R26~R28）
        Assert.Equal("二、试验结果汇总与计算", zh.GetCellValue(24, 1));
        Assert.Equal("统计项目", zh.GetCellValue(25, 1));
        Assert.Equal("稳定度平均值(kN)", zh.GetCellValue(25, 2));
        Assert.Equal("浸水稳定度比(%)", zh.GetCellValue(25, 8));
        Assert.Equal("真空饱水稳定度比(%)", zh.GetCellValue(25, 9));
        Assert.Equal("标准马歇尔组", zh.GetCellValue(26, 1));
        Assert.Equal("{{Result.Group1AvgStability}}", zh.GetCellValue(26, 2));
        Assert.Equal("浸水马歇尔组", zh.GetCellValue(27, 1));
        Assert.Equal("{{Result.ImmersionRatio}}", zh.GetCellValue(27, 8));
        Assert.Equal("真空饱水马歇尔组", zh.GetCellValue(28, 1));
        Assert.Equal("{{Result.VacuumRatio}}", zh.GetCellValue(28, 9));

        // 说明区与签名区（照现役 R30 / R34~R35）
        Assert.Equal("其他需要说明的情况：", zh.GetCellValue(30, 1));
        Assert.Equal("试验人员：", zh.GetCellValue(34, 1));
        Assert.Equal("复核人员：", zh.GetCellValue(34, 4));
        Assert.Equal("监理人员：", zh.GetCellValue(34, 7));
        Assert.Equal("日期：", zh.GetCellValue(35, 1));
        Assert.Equal("{{Sheet.Tester}}", zh.GetCellValue(34, 2));
        Assert.Equal("{{Sheet.TestDate}}", zh.GetCellValue(35, 2));

        // 合并区（照现役）：标题 A1:I1、分节 A9:I9 / A24:I24、说明 A30:B30 与 C30:I33、
        // 报表头的 C2:D2 / F2:I2、日期的 F4:G4、签名区的 H35:I35
        AssertMerged(zh, 1, 1, 1, 9);
        AssertMerged(zh, 2, 3, 2, 4);
        AssertMerged(zh, 2, 6, 2, 9);
        AssertMerged(zh, 4, 6, 4, 7);
        AssertMerged(zh, 9, 1, 9, 9);
        AssertMerged(zh, 24, 1, 24, 9);
        AssertMerged(zh, 30, 1, 30, 2);
        AssertMerged(zh, 30, 3, 33, 9);
        AssertMerged(zh, 35, 8, 35, 9);

        // 英文模板同版式、标签英文（执行标准与仪器设备是模板固定文本）
        var en = new XlsFile();
        en.Open(MarshallTemplates.Resolve(templatesRoot, "en-US"));
        Assert.Equal("Asphalt Mixture Marshall Stability Test Report", en.GetCellValue(1, 1));
        Assert.Equal("Project:", en.GetCellValue(2, 1));
        Assert.Equal("Stability (kN)", en.GetCellValue(10, 5));
        Assert.Equal("Standard Marshall", en.GetCellValue(26, 1));
        Assert.Equal("Supervisor:", en.GetCellValue(34, 7));
    }

    /// <summary>
    /// **样式**照现役逐格对齐（用户 2026-10-15 说过"用代码生成的模板很丑"，此后样式与行高也纳入用例）：
    /// 字体微软雅黑、三处底色、白字、行高与页面设置。
    /// </summary>
    [Fact]
    public void 模板样式应照现役逐格对齐()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var path = MarshallTemplates.Resolve(templatesRoot, "zh-CN");

        var zh = new XlsFile();
        zh.Open(path);

        // 注意：GetCellFormat(row, col) 返回 XF 索引（int），要拿 TFlxFormat 得再 GetFormat(xf)
        TFlxFormat At(int row, int col) => zh.GetFormat(zh.GetCellFormat(row, col));

        // 主标题：微软雅黑 14pt 粗体白字 + 深藏蓝底（现役 f6/s16）
        var title = At(1, 1);
        Assert.Equal("微软雅黑", title.Font.Name);
        Assert.Equal(14 * 20, title.Font.Size20);
        Assert.Equal(TFlxFontStyles.Bold, title.Font.Style);
        Assert.Equal(0xFFFFFFFFL, title.Font.Color.RGB);
        Assert.Equal(TFlxPatternStyle.Solid, title.FillPattern.Pattern);
        Assert.Equal(0xFF003366L, title.FillPattern.FgColor.RGB);

        // 分节标题浅蓝底（R9/R24）、明细与汇总表头蓝底白粗体（R10/R25）
        Assert.Equal(0xFFD9E1F2L, At(9, 1).FillPattern.FgColor.RGB);
        Assert.Equal(0xFFD9E1F2L, At(24, 1).FillPattern.FgColor.RGB);
        Assert.Equal(0xFF0070C0L, At(10, 1).FillPattern.FgColor.RGB);
        Assert.Equal(0xFFFFFFFFL, At(10, 1).Font.Color.RGB);
        Assert.Equal(TFlxFontStyles.Bold, At(10, 1).Font.Style);
        Assert.Equal(0xFF0070C0L, At(25, 1).FillPattern.FgColor.RGB);

        // 行高（FlexCel 单位 = 1/20 磅）：标题 32.25、子项 15、分节 24、表头 33.9
        Assert.Equal(645, zh.GetRowHeight(1));
        Assert.Equal(300, zh.GetRowHeight(2));
        Assert.Equal(480, zh.GetRowHeight(9));
        Assert.Equal(678, zh.GetRowHeight(10));
        Assert.Equal(300, zh.GetRowHeight(11));
        Assert.Equal(678, zh.GetRowHeight(25));

        // 明细区（表头 + 首行 + 末行）四边细边框
        foreach (var row in new[] { 10, 11, 22 })
        {
            var format = At(row, 1);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Left.Style);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Right.Style);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Top.Style);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Bottom.Style);
        }

        // 页面设置照现役实物：A4 横向 + 水平/垂直居中
        Assert.Equal(TPaperSize.A4, zh.PrintPaperSize);
        Assert.True(zh.PrintLandscape);
        Assert.True(zh.PrintHCentered);
        Assert.True(zh.PrintVCentered);
    }

    [Fact]
    public async Task 中英两套模板都应能导出带数据的报表()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0709", "G15 高速 K12+300", "MS");
        await sheets.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["TestStandardsID"] = 1,
                ["TestStandardsDesc"] = "浸水马歇尔试验",
                ["SampleSizeDesc"] = "Φ101.6 × 63.5mm",
                ["TestTemperatureDesc"] = "60",
                ["AcceptanceDate"] = "2026-10-10",
                ["SampleDensity"] = 2.432,
                ["VoidRatio"] = 4.2,
                ["AsphaltUsage"] = 5.1,
                ["ContentByVolume"] = 11.4,
                ["AsphaltSaturation"] = 68.3,
                ["MineralAggregateVoidRatio"] = 15.1,
                ["Tester"] = "王工",
                ["Group1AvgStability"] = 9.5,
                ["Group1Stability_Std_Dev"] = 0.2,
                ["Group1Stability_Cv_Percent"] = 2.1,
                ["Group1AvgFlowValue"] = 3.1,
                ["Group1Flow_Std_Dev"] = 0.1,
                ["Group1Flow_Cv_Percent"] = 3.2,
                ["Group2AvgStability"] = 8.4,
                ["Group2Stability_Std_Dev"] = 0.3,
                ["Group2Stability_Cv_Percent"] = 3.6,
                ["Group2AvgFlowValue"] = 3.6,
                ["Group2Flow_Std_Dev"] = 0.2,
                ["Group2Flow_Cv_Percent"] = 5.5,
                ["StabilityRatio"] = 88.4,
            }
        );

        var curve = Enumerable.Range(0, 60).Select(i => new CurvePoint(i * 0.1, i * 0.15)).ToArray();
        await sheets.AppendSampleAsync(
            sheetId,
            "S001",
            curve,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["GroupID"] = 0,
                ["SampleInitialDiameter"] = 101.6,
                ["SampleInitialHeight"] = 63.5,
                ["MSRaw"] = 9.52,
                ["FlowValue"] = 3.1,
                ["MarshallModulus"] = 3.1,
                ["Effective"] = 1,
            }
        );
        await sheets.AppendSampleAsync(
            sheetId,
            "S002",
            curve,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["GroupID"] = 1,
                ["SampleInitialDiameter"] = 101.6,
                ["SampleInitialHeight"] = 63.4,
                ["MSRaw"] = 8.4,
                ["FlowValue"] = 3.6,
                ["MarshallModulus"] = 2.3,
                ["Effective"] = 0,
            }
        );

        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var reportsRoot = Path.Combine(_directory, "Report");
        var builder = new MarshallReportBuilder(sheets, new FlexCelReportExporter());
        var texts = new FakeTexts();

        var chinese = await builder.BuildAsync(
            sheetId,
            MarshallTemplates.Resolve(templatesRoot, "zh-CN"),
            reportsRoot,
            "zh-CN",
            texts
        );
        var english = await builder.BuildAsync(
            sheetId,
            MarshallTemplates.Resolve(templatesRoot, "en-US"),
            reportsRoot,
            "en-US",
            texts
        );

        Assert.True(File.Exists(chinese.ReportPath));
        Assert.True(File.Exists(english.ReportPath));
        Assert.NotEqual(chinese.ReportPath, english.ReportPath);
        Assert.Equal(2, chinese.CurveImagePaths.Count);

        var zh = new XlsFile();
        zh.Open(chinese.ReportPath);
        Assert.Equal("G15 高速 K12+300", zh.GetCellValue(2, 3)); // 试验项目名称
        Assert.Equal("浸水马歇尔试验", zh.GetCellValue(3, 6)); // 试验方法
        Assert.Equal("60", zh.GetCellValue(7, 3)); // 试验温度
        Assert.Equal("Φ101.6 × 63.5mm", zh.GetCellValue(7, 6)); // 试件尺寸
        Assert.Equal("S001", zh.GetCellValue(11, 1)); // 第 1 件
        Assert.Equal("101.6", zh.GetCellValue(11, 2)); // 直径
        Assert.Equal("标准马歇尔试验", zh.GetCellValue(11, 4)); // 第一组的试验类型
        Assert.Equal("9.52", zh.GetCellValue(11, 5)); // 稳定度 = MSRaw（实测值）
        Assert.Equal("3.10", zh.GetCellValue(11, 6)); // 流值（两位小数）
        Assert.Equal("3.10", zh.GetCellValue(11, 7)); // 马歇尔模数（两位小数）
        Assert.Equal("合格", zh.GetCellValue(11, 8)); // 异常值判断
        Assert.Equal("S002", zh.GetCellValue(12, 1));
        Assert.Equal("浸水马歇尔试验", zh.GetCellValue(12, 4)); // 第二组 = 浸水
        Assert.Equal("剔除", zh.GetCellValue(12, 8)); // Effective = 0
        // 汇总区一律两位小数（用户 2026-10-16：马歇尔的这些量比现役多一位）
        Assert.Equal("9.50", zh.GetCellValue(26, 2)); // 标准组稳定度均值
        Assert.Equal("8.40", zh.GetCellValue(27, 2)); // 浸水组稳定度均值
        Assert.Equal("88.40", zh.GetCellValue(27, 8)); // 浸水稳定度比落 H27
        Assert.Equal(string.Empty, Convert.ToString(zh.GetCellValue(28, 9))); // 真空那一格不写

        // 试验日期只印日期（用户 2026-10-15 的定例），签名行同样
        var today = DateTime.Now.ToString(
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture
        );
        Assert.Equal(today, zh.GetCellValue(4, 9));
        Assert.Equal(today, zh.GetCellValue(35, 2));

        var en = new XlsFile();
        en.Open(english.ReportPath);
        Assert.Equal("Project:", en.GetCellValue(2, 1));
        Assert.Equal("Stable", en.GetCellValue(11, 8)); // FakeTexts 的英文"合格"
        Assert.Equal("S002", en.GetCellValue(12, 1));
    }

    /// <summary>明细超过模板预排的 12 行时自动插行，汇总/说明/签名/图片区整体下移，且表尾嵌曲线图。</summary>
    [Fact]
    public async Task 明细超过十二件应自动插行且表尾嵌曲线图()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0709", "G15 高速 K12+300", "MS");

        const int count = 15;
        const int inserted = count - 12;
        var curve = Enumerable.Range(0, 60).Select(i => new CurvePoint(i * 0.1, i * 0.15)).ToArray();

        for (var index = 1; index <= count; index++)
        {
            await sheets.AppendSampleAsync(
                sheetId,
                $"S{index:000}",
                curve,
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleInitialDiameter"] = 101.6,
                    ["SampleInitialHeight"] = 63.5,
                    ["MSRaw"] = 8 + index,
                    ["Effective"] = 1,
                }
            );
        }

        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var builder = new MarshallReportBuilder(sheets, new FlexCelReportExporter());
        var result = await builder.BuildAsync(
            sheetId,
            MarshallTemplates.Resolve(templatesRoot, "zh-CN"),
            Path.Combine(_directory, "Report"),
            "zh-CN",
            new FakeTexts()
        );

        var zh = new XlsFile();
        zh.Open(result.ReportPath);

        // 明细：第 11 行起 15 件，最后一件落在 11+14 = 25 行（模板只预排到 22 行）
        Assert.Equal("S001", zh.GetCellValue(11, 1));
        Assert.Equal("S015", zh.GetCellValue(25, 1));
        Assert.Equal("23.00", zh.GetCellValue(25, 5)); // 稳定度 = 8 + 15（两位小数）

        // 插了 3 行 → 汇总表头 25→28、说明区 30→33、签名 34/35→37/38、图片标题 37→40
        Assert.Equal("统计项目", zh.GetCellValue(28, 1));
        Assert.Equal("其他需要说明的情况：", zh.GetCellValue(33, 1));
        Assert.Equal("试验人员：", zh.GetCellValue(37, 1));
        Assert.Equal("三、试验曲线图", zh.GetCellValue(40, 1));

        // 曲线图：15 张全嵌进来，两张一行、竖向往下
        Assert.Equal(count, zh.ImageCount);
        var first = zh.GetImageProperties(1).Anchor;
        var second = zh.GetImageProperties(2).Anchor;
        var third = zh.GetImageProperties(3).Anchor;
        Assert.Equal(first.Row1, second.Row1);
        Assert.True(first.Col1 < second.Col1);
        Assert.True(third.Row1 > first.Row1);
    }

    /// <summary>这次没有曲线图时，表尾图片区（含"三、试验曲线图"标题行）整段删掉，不留空标题。</summary>
    [Fact]
    public async Task 没有曲线图时应删掉表尾图片区()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0709", "G15 高速 K12+300", "MS");
        await sheets.AppendSampleAsync(
            sheetId,
            "S001",
            [],
            new Dictionary<string, double>(StringComparer.Ordinal) { ["MSRaw"] = 9.5 }
        );

        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var builder = new MarshallReportBuilder(sheets, new FlexCelReportExporter());
        var result = await builder.BuildAsync(
            sheetId,
            MarshallTemplates.Resolve(templatesRoot, "zh-CN"),
            Path.Combine(_directory, "Report"),
            "zh-CN",
            new FakeTexts()
        );

        var zh = new XlsFile();
        zh.Open(result.ReportPath);

        Assert.Equal(0, zh.ImageCount);
        Assert.Equal(string.Empty, Convert.ToString(zh.GetCellValue(37, 1))); // 标题行没了
        Assert.Equal("其他需要说明的情况：", zh.GetCellValue(30, 1)); // 说明区没被挪动
        Assert.Equal("试验人员：", zh.GetCellValue(34, 1));
    }

    /// <summary>断言 (row,col) 属于某个合并区。</summary>
    private static void AssertMerged(XlsFile workbook, int row1, int col1, int row2, int col2)
    {
        var bounds = workbook.CellMergedBounds(row1, col1);
        Assert.NotNull(bounds);
        Assert.Equal(row1, bounds.Top);
        Assert.Equal(col1, bounds.Left);
        Assert.Equal(row2, bounds.Bottom);
        Assert.Equal(col2, bounds.Right);
    }

    /// <summary>最小文案解析器（只给报表用到的那几条键）。</summary>
    private sealed class FakeTexts : ITextResolver
    {
        public string Get(string key, string culture) =>
            (key, culture) switch
            {
                ("Method.T0709.ValidityNormal", "en-US") => "Stable",
                ("Method.T0709.ValidityAbnormal", "en-US") => "Rejected",
                ("Method.T0709.ValidityNormal", _) => "合格",
                ("Method.T0709.ValidityAbnormal", _) => "剔除",
                ("Method.T0709.TestTypeStandard", "en-US") => "Marshall test",
                ("Method.T0709.TestTypeImmersion", "en-US") => "Soaked Marshall test",
                ("Method.T0709.TestTypeVacuum", "en-US") => "Vacuum-soaked Marshall test",
                ("Method.T0709.TestTypeStandard", _) => "标准马歇尔试验",
                ("Method.T0709.TestTypeImmersion", _) => "浸水马歇尔试验",
                ("Method.T0709.TestTypeVacuum", _) => "真空饱水马歇尔试验",
                _ => key,
            };
    }

    private TestSheetService CreateService()
    {
        var module = new MarshallPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();

        return new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
    }
}
