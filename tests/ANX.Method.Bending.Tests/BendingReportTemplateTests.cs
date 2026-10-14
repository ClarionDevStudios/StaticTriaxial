namespace ANX.Method.Bending.Tests;

using ANX.Core;
using ANX.Core.Reporting;
using ANX.Storage;
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
        Assert.Equal("JTC3410-2025 / T0715-2025 沥青混合料弯曲试验", zh.GetCellValue(2, 6));
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
            await sheets.AppendSampleAsync(
                sheetId,
                sampleId,
                curve,
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["SampleWidth"] = 30,
                    ["SampleHeight"] = 35,
                    ["PeakLoadValue"] = 9.0,
                    ["CorrectedDeflection"] = 4.5,
                    ["FlexuralStrength"] = 73.47,
                    ["MaxBendingTensileStrain"] = 23625,
                }
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
        // 试件长度：本软件的试样信息只有跨径/宽/高，明细没填 SampleLength 就**如实留空**
        //（跨径 200 ≠ 试件长度 250，不拿它兜底）
        Assert.Equal(string.Empty, Convert.ToString(zh.GetCellValue(11, 2)));
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
