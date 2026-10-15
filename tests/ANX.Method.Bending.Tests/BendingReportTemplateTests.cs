namespace ANX.Method.Bending.Tests;

using ANX.Core;
using ANX.Core.Reporting;
using ANX.Storage;
using FlexCel.Core;
using FlexCel.XlsAdapter;
using Microsoft.Data.Sqlite;

/// <summary>
/// 出厂模板（中英两套）与"从历史页导出报表"这条链路：
/// 模板不存在时程序自己写出 → 按语种选模板 → 装配 → 出 Excel。
/// </summary>
public sealed class BendingReportTemplateTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"anx-tpl-{Guid.NewGuid():N}"
    );

    public BendingReportTemplateTests() => Directory.CreateDirectory(_directory);

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

        BendingTemplates.EnsureDefaultTemplates(templatesRoot);
        Assert.True(File.Exists(Path.Combine(templatesRoot, "Bending.zh-CN.xlsx")));
        Assert.True(File.Exists(Path.Combine(templatesRoot, "Bending.en-US.xlsx")));

        // 幂等：再调一次不重写、不报错
        BendingTemplates.EnsureDefaultTemplates(templatesRoot);

        Assert.Equal(
            Path.Combine(templatesRoot, "Bending.en-US.xlsx"),
            BendingTemplates.Resolve(templatesRoot, "en-US")
        );
        // 语种不认识时回落到中文
        Assert.Equal(
            Path.Combine(templatesRoot, "Bending.zh-CN.xlsx"),
            BendingTemplates.Resolve(templatesRoot, "ja-JP")
        );
    }

    /// <summary>
    /// 装机里那份模板落后于本版时必须**自动换代**：备份旧的、写出新的。
    /// 这条是 2026-10-15 补的机制 —— 原先只判"文件在不在"，于是 10-14 改了版式之后，
    /// 装机那份 10-11 的旧模板一直没被换掉，软件照旧导出旧版式的报表。
    /// </summary>
    [Fact]
    public void 版本戳落后的模板应在导出前换代并备份()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        BendingTemplates.EnsureDefaultTemplates(templatesRoot);

        var path = Path.Combine(templatesRoot, "Bending.zh-CN.xlsx");

        // 模拟装机里那份"没有戳的旧模板"（旧版式就没有这个戳）
        OverwriteAfterEdit(path, workbook => workbook.SetCellValue(1, 11, string.Empty));

        BendingTemplates.EnsureDefaultTemplates(templatesRoot);

        // 换代了：文件回来带本版戳
        var refreshed = new XlsFile();
        refreshed.Open(path);
        Assert.Equal($"ANX-TEMPLATE-V{BendingTemplates.TemplateVersion}", refreshed.GetCellValue(1, 11));

        // 旧的那份**没被删**，改名备份在同目录
        var backups = Directory.GetFiles(templatesRoot, "Bending.zh-CN.xlsx.*.bak.xlsx");
        Assert.Single(backups);
        var backup = new XlsFile();
        backup.Open(backups[0]);
        Assert.Equal(string.Empty, Convert.ToString(backup.GetCellValue(1, 11)));
    }

    /// <summary>戳一致时**不覆盖**：站点手工改过的模板继续生效（换代才让位，且会先备份）。</summary>
    [Fact]
    public void 版本戳一致时手工改过的模板不应被覆盖()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        BendingTemplates.EnsureDefaultTemplates(templatesRoot);

        var path = Path.Combine(templatesRoot, "Bending.zh-CN.xlsx");
        OverwriteAfterEdit(path, workbook => workbook.SetCellValue(27, 3, "现场备注：本机试验室编号 A-12"));

        BendingTemplates.EnsureDefaultTemplates(templatesRoot);

        var reopened = new XlsFile();
        reopened.Open(path);
        Assert.Equal("现场备注：本机试验室编号 A-12", reopened.GetCellValue(27, 3));
        Assert.Empty(Directory.GetFiles(templatesRoot, "Bending.zh-CN.xlsx.*.bak.xlsx"));
    }

    /// <summary>
    /// 把模板改一处再顶上（模拟"装机里那份旧模板"或"用户手工改过模板"）。
    /// 走"另存 + 换名"而不是就地 Save：**FlexCel 的 <c>Save(路径)</c> 不允许覆盖已存在的文件**
    /// （会抛 <c>IOException: The file ... already exists</c>）。
    /// </summary>
    private static void OverwriteAfterEdit(string path, Action<XlsFile> edit)
    {
        var workbook = new XlsFile();
        workbook.Open(path);
        edit(workbook);

        var staged = path + ".staged";
        workbook.Save(staged);
        File.Move(staged, path, overwrite: true);
    }

    /// <summary>
    /// **样式**也要照现役逐格对齐 —— 2026-10-15 补的用例。
    /// 起因：用户看过实物报表后说"用代码生成的模板很丑"。查下来上一轮的用例只断言"单元格文字"，
    /// 于是"字体抄成宋体、三处底色和全部行高整个没抄"这种错也能全绿通过。
    /// </summary>
    [Fact]
    public void 模板样式应照现役逐格对齐()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var path = BendingTemplates.Resolve(templatesRoot, "zh-CN");

        var zh = new XlsFile();
        zh.Open(path);

        // 注意：`GetCellFormat(row, col)` 返回的是 **XF 索引**（int），要拿 TFlxFormat 得再 `GetFormat(xf)`
        TFlxFormat At(int row, int col) => zh.GetFormat(zh.GetCellFormat(row, col));

        // 主标题：微软雅黑 16pt 粗体白字 + 深藏蓝底
        var title = At(1, 1);
        Assert.Equal("微软雅黑", title.Font.Name);
        Assert.Equal(16 * 20, title.Font.Size20);
        Assert.Equal(TFlxFontStyles.Bold, title.Font.Style);
        // `RGB` 是 **Int64** 且带 alpha（0xFFFFFFFF），不是裸 RGB
        Assert.Equal(0xFFFFFFFFL, title.Font.Color.RGB);
        Assert.Equal(TFlxPatternStyle.Solid, title.FillPattern.Pattern);
        Assert.Equal(0xFF003366L, title.FillPattern.FgColor.RGB);

        // 分节标题浅蓝底、明细表头蓝底白粗体
        Assert.Equal(0xFFD9E1F2L, At(9, 1).FillPattern.FgColor.RGB);
        Assert.Equal(0xFF0070C0L, At(10, 1).FillPattern.FgColor.RGB);
        Assert.Equal(0xFFFFFFFFL, At(10, 1).Font.Color.RGB);
        Assert.Equal(TFlxFontStyles.Bold, At(10, 1).Font.Style);

        // 行高：标题 38.1、分节 24、表头 33.9、其余 15（FlexCel 单位 = 1/20 磅）
        Assert.Equal(762, zh.GetRowHeight(1));
        Assert.Equal(300, zh.GetRowHeight(2));
        Assert.Equal(480, zh.GetRowHeight(9));
        Assert.Equal(678, zh.GetRowHeight(10));
        Assert.Equal(300, zh.GetRowHeight(25));

        // 明细区（含表头与最后一行）四边细边框
        foreach (var row in new[] { 10, 11, 25 })
        {
            var format = At(row, 1);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Left.Style);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Right.Style);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Top.Style);
            Assert.Equal(TFlxBorderStyle.Thin, format.Borders.Bottom.Style);
        }
    }

    [Fact]
    public void 模板版式照现役报表()
    {
        var templatesRoot = Path.Combine(_directory, "Report", "Template");
        var path = BendingTemplates.Resolve(templatesRoot, "zh-CN");

        var zh = new XlsFile();
        zh.Open(path);

        // 报表头（行 2~8）：标签在 A/E/H 列、值区在 C/F/I 列（照现役 T0715-2025Template.xlsx）
        Assert.Equal("沥青混合料弯曲试验报表", zh.GetCellValue(1, 1));
        Assert.Equal("试验项目名称：", zh.GetCellValue(2, 1));
        Assert.Equal("{{Sheet.ProjectName}}", zh.GetCellValue(2, 3));
        Assert.Equal("执行标准：", zh.GetCellValue(2, 5));
        // 规范正式名称是 JTG3410-2025（现役模板里的 JTC 是笔误，用户 2026-10-15 定为改正）
        Assert.Equal("JTG3410-2025 / T0715-2025 沥青混合料弯曲试验", zh.GetCellValue(2, 6));
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

        // 分节标题、明细表头（9 列，末列"异常值判断"）
        Assert.Equal("一、试件基本物理指标及试验结果", zh.GetCellValue(9, 1));
        Assert.Equal("试件编号", zh.GetCellValue(10, 1));
        Assert.Equal("试件长度(mm)", zh.GetCellValue(10, 2));
        Assert.Equal("跨中挠度(mm)", zh.GetCellValue(10, 5));
        Assert.Equal("破坏时荷载(kN)", zh.GetCellValue(10, 6));
        Assert.Equal("异常值判断", zh.GetCellValue(10, 9));

        // 明细模板行（第 11 行）放占位符；12~25 行是空的带框行（明细不足 15 件时框线仍完整）
        Assert.Equal("{{Detail.SampleID}}", zh.GetCellValue(11, 1));
        Assert.Equal("{{Detail.EffectiveText}}", zh.GetCellValue(11, 9));
        Assert.Equal("", Convert.ToString(zh.GetCellValue(12, 1)));

        // 说明区与签名行（照现役）
        Assert.Equal("其他需要说明的情况：", zh.GetCellValue(27, 1));
        Assert.Equal("试验人员：", zh.GetCellValue(31, 1));
        Assert.Equal("复核人员：", zh.GetCellValue(31, 4));
        Assert.Equal("监理人员：", zh.GetCellValue(31, 7));
        Assert.Equal("日期：", zh.GetCellValue(32, 1));
        Assert.Equal("{{Sheet.Operator}}", zh.GetCellValue(31, 2));

        // 合并区（照现役）：标题横跨 A1:I1、说明值区 C27:I30、表头行 9 横跨 A9:I9
        AssertMerged(zh, 1, 1, 1, 9);
        AssertMerged(zh, 9, 1, 9, 9);
        AssertMerged(zh, 27, 3, 30, 9);
        AssertMerged(zh, 2, 3, 2, 4);

        // 英文模板同版式、标签英文（执行标准与仪器设备是模板固定文本）
        var en = new XlsFile();
        en.Open(BendingTemplates.Resolve(templatesRoot, "en-US"));
        Assert.Equal("Asphalt Mixture Bending Test Report", en.GetCellValue(1, 1));
        Assert.Equal("Project name:", en.GetCellValue(2, 1));
        Assert.Equal("Validity", en.GetCellValue(10, 9));
        Assert.Equal("Supervisor:", en.GetCellValue(31, 7));
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

    [Fact]
    public async Task 中英两套模板都应能导出带数据的报表()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0715", "G15 高速 K12+300", "WT");
        await sheets.UpdateMainFieldsAsync(
            sheetId,
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["TestTemperature"] = 15,
                ["LoadRate"] = 50,
                ["SpanLength"] = 200,
                ["EffectiveCount"] = 2,
                ["FlexuralStrengthAvg"] = 73.79,
                ["MaxBendingStrainAvg"] = 23663,
            }
        );

        var curve = Enumerable.Range(0, 60).Select(i => new CurvePoint(i * 0.1, i * 0.15)).ToArray();
        foreach (var sampleId in new[] { "1", "2" })
        {
            var columns = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["SampleWidth"] = 30,
                ["SampleHeight"] = 35,
                ["PeakLoadValue"] = 9.0,
                ["CorrectedDeflection"] = 4.5,
                ["FlexuralStrength"] = 73.47,
                ["MaxBendingTensileStrain"] = 23625,
            };

            // 只给第 1 件记长度：一份用例同时验"记了就印出来"和"没记就如实留空"两种情况
            // （不能给第 2 件写 0 —— 那是"长度为 0"，报表会印出 0）
            if (sampleId == "1")
            {
                columns["SampleLength"] = 250;
            }

            await sheets.AppendSampleAsync(
                sheetId,
                sampleId,
                curve,
                columns
            );
        }

        var reportsRoot = Path.Combine(_directory, "Report");
        // 传一个最小文案解析器：报表里的"异常值判断"（正常/异常）走资源键，无解析器会显示键名
        var export = new BendingReportExportService(sheets, reportsRoot, new FakeTexts());

        var chinese = await export.ExportAsync(sheetId, "zh-CN");
        var english = await export.ExportAsync(sheetId, "en-US");

        Assert.True(File.Exists(chinese));
        Assert.True(File.Exists(english));
        Assert.NotEqual(chinese, english);

        // 中文报表：照现役版式 —— 表头在行 2~8、明细从第 11 行起按行展开
        var zh = new XlsFile();
        zh.Open(chinese);
        Assert.Equal("G15 高速 K12+300", zh.GetCellValue(2, 3)); // 试验项目名称
        Assert.Equal("15", zh.GetCellValue(7, 3)); // 试验温度
        Assert.Equal("1", zh.GetCellValue(11, 1)); // 第 1 件：试件编号
        // 试件长度：记了就有（250），没记就**如实留空** —— 不拿跨径 200 兜底充数
        Assert.Equal("250", zh.GetCellValue(11, 2));
        Assert.Equal(string.Empty, Convert.ToString(zh.GetCellValue(12, 2)));
        Assert.Equal("30", zh.GetCellValue(11, 3)); // 试件宽度
        Assert.Equal("73.47", zh.GetCellValue(11, 7)); // 弯拉强度
        Assert.Equal("23625", zh.GetCellValue(11, 8)); // 最大弯拉应变
        Assert.Equal("正常", zh.GetCellValue(11, 9)); // 异常值判断
        Assert.Equal("2", zh.GetCellValue(12, 1)); // 第 2 件换到下一行

        // 英文报表：同一份数据、英文标签
        var en = new XlsFile();
        en.Open(english);
        Assert.Equal("Project name:", en.GetCellValue(2, 1));
        Assert.Equal("G15 高速 K12+300", en.GetCellValue(2, 3));
        Assert.Equal("2", en.GetCellValue(12, 1));
        Assert.Equal("Normal", en.GetCellValue(11, 9));
    }

    /// <summary>
    /// 同一张单、同一语种导出第二次必须成功（覆盖上一次的产物）—— 用户会反复导
    ///（执行页「打印报表」、历史页「导出」都可能点第二次）。这条是探针式的回归：
    /// **FlexCel 的 <c>Save(路径)</c> 对已存在的文件是"拒绝"而不是"覆盖"**（抛 <c>IOException</c>）。
    /// </summary>
    [Fact]
    public async Task 同一张单重复导出应覆盖上一次的产物()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0715", "G15 高速 K12+300", "WT");
        await sheets.AppendSampleAsync(
            sheetId,
            "1",
            [new CurvePoint(0, 0), new CurvePoint(1, 1), new CurvePoint(2, 2)],
            new Dictionary<string, double>(StringComparer.Ordinal) { ["SampleWidth"] = 30 }
        );

        var export = new BendingReportExportService(
            sheets,
            Path.Combine(_directory, "Report"),
            new FakeTexts()
        );

        var first = await export.ExportAsync(sheetId, "zh-CN");
        var second = await export.ExportAsync(sheetId, "zh-CN");

        Assert.Equal(first, second);
        Assert.True(File.Exists(second));
    }

    /// <summary>
    /// 明细超过模板预排的 15 行时**自动插行**，说明区 / 签名行 / 曲线图区整体跟着下移
    /// （用户 2026-10-15："不限制试样信息行数……在试样信息输出完毕后，在输出剩余内容"）。
    /// 同时验曲线图真的嵌进表尾：两张一行、竖向往下。
    /// </summary>
    [Fact]
    public async Task 明细超过十五件应自动插行且表尾嵌曲线图()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0715", "G15 高速 K12+300", "WT");

        const int count = 18;
        const int inserted = count - 15;
        var curve = Enumerable.Range(0, 60).Select(i => new CurvePoint(i * 0.1, i * 0.15)).ToArray();

        for (var index = 1; index <= count; index++)
        {
            await sheets.AppendSampleAsync(
                sheetId,
                // 用应用里那种零填充试件号：明细是按试件号取的，写 "1".."18" 会排成 1,10,11,…,2,3…（踩过）
                $"S{index:000}",
                curve,
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleLength"] = 250,
                    ["SampleWidth"] = 30,
                    ["SampleHeight"] = 35,
                    ["FlexuralStrength"] = 70 + index,
                }
            );
        }

        var export = new BendingReportExportService(
            sheets,
            Path.Combine(_directory, "Report"),
            new FakeTexts()
        );
        var path = await export.ExportAsync(sheetId, "zh-CN");

        var zh = new XlsFile();
        zh.Open(path);

        // 明细：第 11 行起 18 件，最后一件落在 11+17 = 28 行（模板只预排到 25 行）
        Assert.Equal("S001", zh.GetCellValue(11, 1));
        Assert.Equal("S018", zh.GetCellValue(28, 1));
        Assert.Equal("88", zh.GetCellValue(28, 7)); // 弯拉强度 = 70 + 18
        Assert.Equal("250", zh.GetCellValue(28, 2)); // 试件长度列

        // 插了 3 行 → 下面三块各下移 3 行（27→30、31/32→34/35、图片标题 34→37）
        Assert.Equal("其他需要说明的情况：", zh.GetCellValue(27 + inserted, 1));
        Assert.Equal("试验人员：", zh.GetCellValue(31 + inserted, 1));
        Assert.Equal("二、试验曲线图", zh.GetCellValue(34 + inserted, 1));

        // 曲线图：18 张全嵌进来，两张一行、竖向往下
        Assert.Equal(count, zh.ImageCount);
        // 图片索引是 **1 基**（1..ImageCount），写 0 会抛 "ImageIndex: 0 is not on the range 1 - 18"
        var first = zh.GetImageProperties(1).Anchor;
        var second = zh.GetImageProperties(2).Anchor;
        var third = zh.GetImageProperties(3).Anchor;

        Assert.Equal(first.Row1, second.Row1); // 头两张同一排
        Assert.True(first.Col1 < second.Col1); // 左右两列
        Assert.True(third.Row1 > first.Row1); // 第三张换到下一排
        Assert.True(first.Row2 > first.Row1); // 图有高度
    }

    /// <summary>
    /// 这次没有曲线图时，表尾那块图片区（含"二、试验曲线图"标题行）要**整段删掉**，不留空标题。
    /// </summary>
    [Fact]
    public async Task 没有曲线图时应删掉表尾图片区()
    {
        var sheets = CreateService();
        var sheetId = await sheets.CreateAsync("T0715", "G15 高速 K12+300", "WT");

        // 空曲线 → 不出 PNG（SaveCurveImage 对没有曲线的件不写空图）
        await sheets.AppendSampleAsync(
            sheetId,
            "1",
            [],
            new Dictionary<string, double>(StringComparer.Ordinal) { ["FlexuralStrength"] = 73.47 }
        );

        var export = new BendingReportExportService(
            sheets,
            Path.Combine(_directory, "Report"),
            new FakeTexts()
        );
        var path = await export.ExportAsync(sheetId, "zh-CN");

        var zh = new XlsFile();
        zh.Open(path);

        Assert.Equal(0, zh.ImageCount);
        Assert.Equal(string.Empty, Convert.ToString(zh.GetCellValue(34, 1))); // 标题行没了
        Assert.Equal("其他需要说明的情况：", zh.GetCellValue(27, 1)); // 说明区没被挪动
        Assert.Equal("试验人员：", zh.GetCellValue(31, 1));
    }

    /// <summary>最小文案解析器（只给报表用到的那两个键）。</summary>
    private sealed class FakeTexts : ITextResolver
    {
        public string Get(string key, string culture) =>
            (key, culture) switch
            {
                ("Method.T0715.ValidityNormal", "en-US") => "Normal",
                ("Method.T0715.ValidityAbnormal", "en-US") => "Abnormal",
                ("Method.T0715.ValidityNormal", _) => "正常",
                ("Method.T0715.ValidityAbnormal", _) => "异常",
                _ => key,
            };
    }

    private TestSheetService CreateService()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_directory, "anx.db")),
            [module]
        );
        initializer.Initialize();

        return new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
    }
}
