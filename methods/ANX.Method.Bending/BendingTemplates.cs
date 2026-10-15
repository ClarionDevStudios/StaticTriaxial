namespace ANX.Method.Bending;

using ANX.Reporting;
using FlexCel.Core;
using FlexCel.XlsAdapter;

/// <summary>
/// T 0715 的**出厂报表模板**（标签制，中英两套）。
///
/// **版式照现役 Delphi 的报表**（用户 2026-10-14："报表模板按照现役软件的格式改写"）：
/// 照抄现役模板 `Bins\Report\Template\T0715-2025Template.xlsx` 的单元格、合并区、列宽与格式 ——
/// 现役那张是"表单式"报表（A1:I32）：报表头行 2~8（标签在 A/E/H、值在 C/F/I）、
/// 分节标题行 9、明细表头行 10、明细数据行 11~25、说明区（C27:I30 合并）、签名行 31 与日期行 32。
///
/// **明细行数不再有上限**（用户 2026-10-15）：模板里先排 15 行带框的明细行，装不下时由导出器
/// **按需插行**（复制块尾那一行的格式），下面的说明区、签名与图片区跟着整体下移。
/// 我们自己在这张表尾加了**曲线图区**（R33 空行 + R34 标题行带 `{{Images}}` 标记，图从 R35 起）——
/// 这一块现役没有（现役是把曲线 PNG 放在报表同目录、不嵌进表里），是本项目按用户要求新增的。
///
/// 与现役的**唯一差别**是填值方式：现役按"行/列坐标"写，我们按 `{{标签}}` 写
/// （模板里的值区放占位符，导出器按标签替换）—— 这样版式与填值机制各自独立，
/// 现场改模板不影响代码。**曲线图不嵌在报表里**（现役同样是把 PNG 放在报表同目录，见 SaveCurveImage）。
///
/// 固定文本（执行标准 / 仪器设备名称 / 型号 / 设备编号）与本文件里的常量一致，可在模板文件里直接改。
///
/// **模板换代**（2026-10-15 补的机制）：模板文件里藏在隐藏列 K 的版本戳（见 <see cref="TemplateVersion"/>）
/// 与本文件不一致时，会把磁盘上那份**改名备份**再重写出厂模板；戳一致就原样保留（站点改过的模板继续生效）。
/// 踩过的坑：原先只判"文件在不在" —— 于是 10-14 改了版式之后，装机那份 10-11 的旧模板一直没被换掉，
/// 软件照旧导出旧版式的报表，改动看着"没生效"。
/// </summary>
public static class BendingTemplates
{
    /// <summary>模板文件名的语种后缀。</summary>
    public const string ChineseCulture = "zh-CN";

    public const string EnglishCulture = "en-US";

    /// <summary>
    /// **出厂模板版本号**：改了模板的单元格结构、固定文本或版式就得 +1。
    ///
    /// 用法：导出前比对模板里隐藏列 K 的戳 —— 落后就"备份旧的 + 重写出厂模板"，一致就原样保留
    /// （站点手工改过的模板不会被无声覆盖；真换代时改动在那份 `.bak.xlsx` 里）。
    ///
    /// 版本沿革：
    /// <list type="bullet">
    ///   <item>v1（无戳，2026-10-11）：旧版式 —— "试验单号 / 有效试件数 / 弯拉强度平均值"那套横向排布；</item>
    ///   <item>v2（2026-10-15）：表单式 A1:I32（照现役）+ 执行标准改正为 `JTG3410-2025`。**没有戳的旧文件一律当要换代。**</item>
    ///   <item>v3（2026-10-15，用户："模板很丑，我不满意"）：把**样式逐格照现役抄全** ——
    ///         字体改回**微软雅黑**（v2 误写成宋体）、补上三处底色（标题 #003366 / 分节 #D9E1F2 / 表头 #0070C0）、
    ///         标题与表头白字、以及**全部显式行高**（38.1 / 24 / 33.9 / 15）。</item>
    ///   <item>v4（2026-10-15，用户："不限制试样信息行数" + "表尾加曲线图"）：明细行数不再有上限
    ///         （导出器按需插行，下面的说明区/签名/图片区跟着下移），并在表尾加**曲线图区**：
    ///         标题行 R34 + `{{Images}}` 标记，图从 R35 起两张一行往下排。</item>
    ///   <item>v5（2026-10-15，用户问"报表纸张方向是横向吗"）：补上**页面设置** —— 照现役 8 份模板的实物
    ///         （A4 横向 + 水平/垂直居中 + 压小的页边距）；此前我们的模板没设，默认是纵向 A4。</item>
    /// </list>
    /// </summary>
    public const int TemplateVersion = 5;

    /// <summary>出厂模板管理（版本戳 / 换代备份 / 按语种确保最新）—— 通用件，各方法共用同一条口径。</summary>
    private static FactoryTemplateStore Store(string templatesRoot) =>
        new(templatesRoot, "Bending", TemplateVersion, [ChineseCulture, EnglishCulture], Write);

    /// <summary>
    /// 字体：现役整套用的是**微软雅黑**（2026-10-15 逐格读出来的；此前误抄成"宋体"）。
    /// 中英两套都用它 —— 英文也用雅黑渲染，两套报表的观感才一致。
    /// </summary>
    private const string TemplateFontName = "微软雅黑";

    /// <summary>主标题底色：现役 A1 是深藏蓝 `#003366` + 16pt 白色粗体。</summary>
    private static readonly TExcelColor TitleFill = TExcelColor.FromArgb(0x00, 0x33, 0x66);

    /// <summary>分节标题底色：现役 A9 是浅蓝 `#D9E1F2`。</summary>
    private static readonly TExcelColor SectionFill = TExcelColor.FromArgb(0xD9, 0xE1, 0xF2);

    /// <summary>明细表头底色：现役 R10 是蓝 `#0070C0` + 白色粗体。</summary>
    private static readonly TExcelColor HeaderFill = TExcelColor.FromArgb(0x00, 0x70, 0xC0);

    private static readonly TExcelColor White = TExcelColor.FromArgb(0xFF, 0xFF, 0xFF);

    /// <summary>
    /// 行高换算：FlexCel 的行高单位是 **1/20 磅**（现役 ht 值就是磅，15 磅 → 300）。
    /// 现役的行高：标题 38.1、分节标题 24、明细表头 33.9、其余 15。
    /// </summary>
    private static int RowHeight(double points) => (int)Math.Round(points * 20);

    /// <summary>
    /// 执行标准：规范正式名称为 `JTG 3410-2025`。
    /// 现役模板写的是 `JTC3410-2025`（笔误），按用户 2026-10-15 的裁定**改正**，不照抄笔误
    /// —— 这是与现役版式的唯一一处**文字**差异（版式仍逐格一致）。
    /// </summary>
    private const string StandardTextZh = "JTG3410-2025 / T0715-2025 沥青混合料弯曲试验";

    private const string StandardTextEn = "JTG3410-2025 / T0715-2025 Asphalt Mixture Bending Test";

    /// <summary>仪器设备信息：现役在报表代码里写死的常量（用户现场那台设备）。</summary>
    private const string InstrumentNameZh = "沥青综合性能试验仪";

    private const string InstrumentNameEn = "Asphalt comprehensive tester";

    private const string InstrumentModel = "ZY-64";

    private const string InstrumentNo = "JLMS-2024-001";

    /// <summary>
    /// 确保两套模板都是**本版出厂模板**；返回中文模板路径。
    /// 规则（缺 → 写、旧 → 备份后重写、本版 → 留着）在 <see cref="FactoryTemplateStore"/> 里，各方法共用。
    /// </summary>
    public static string EnsureDefaultTemplates(string templatesRoot)
    {
        Store(templatesRoot).EnsureAll();

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

        // ── 格式：全部照现役 `T0715-2025Template.xlsx` **逐格读出来**的
        //    （字体 / 底色 / 字色 / 对齐 / 边框；行高在下面单独设）──
        //    2026-10-15 更正：上一轮只抄了字体名（还抄错了：写成"宋体"，现役是**微软雅黑**）、
        //    对齐与边框，**底色和行高根本没抄** —— 所以生成的模板跟现役完全不是一回事。
        var titleFormat = AddFormat(
            workbook,
            TemplateFontName,
            16,
            bold: true,
            THFlxAlignment.center,
            TVFlxAlignment.center,
            fontColor: White,
            fillColor: TitleFill
        );
        var labelFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: true,
            THFlxAlignment.right,
            TVFlxAlignment.center
        );
        var valueFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: false,
            THFlxAlignment.left,
            TVFlxAlignment.center
        );
        var standardFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: true,
            THFlxAlignment.left,
            TVFlxAlignment.center
        );
        var sectionFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: true,
            THFlxAlignment.left,
            TVFlxAlignment.center,
            fillColor: SectionFill
        );
        var headerFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: true,
            THFlxAlignment.center,
            TVFlxAlignment.center,
            tableBorder: true,
            fontColor: White,
            fillColor: HeaderFill
        );
        var cellFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: false,
            THFlxAlignment.center,
            TVFlxAlignment.center,
            tableBorder: true
        );
        var noteFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: false,
            THFlxAlignment.left,
            TVFlxAlignment.center,
            tableBorder: true
        );
        var signValueFormat = AddFormat(
            workbook,
            TemplateFontName,
            11,
            bold: false,
            THFlxAlignment.general,
            TVFlxAlignment.bottom,
            bottomLine: true
        );

        // ── 列宽（照现役模板）──
        foreach (var (col, width) in ColumnWidths())
        {
            workbook.SetColWidth(col, (int)Math.Round(width * 256));
        }

        // ── 行高（照现役模板：标题 38.1、分节标题 24、明细表头 33.9，其余一律 15）──
        //    不设行高时 Excel 用默认的 14.25 左右，标题与表头会"扁"，是看着不像现役的一大原因。
        workbook.SetRowHeight(1, RowHeight(38.1));
        for (var row = 2; row <= 8; row++)
        {
            workbook.SetRowHeight(row, RowHeight(15));
        }
        workbook.SetRowHeight(9, RowHeight(24));
        workbook.SetRowHeight(10, RowHeight(33.9));
        for (var row = 11; row <= 33; row++)
        {
            workbook.SetRowHeight(row, RowHeight(15));
        }
        workbook.SetRowHeight(34, RowHeight(24)); // 图片区标题行，与"一、…"分节标题同高

        // ── 标题（合并 A1:I1）──
        workbook.SetCellValue(1, 1, T("沥青混合料弯曲试验报表", "Asphalt Mixture Bending Test Report"));
        workbook.MergeCells(1, 1, 1, 9);
        Style(workbook, titleFormat, 1, 1, 1, 9);

        // ── 报表头（行 2~8）：标签在 A/E/H 列（粗体右对齐），值在 C/F/I 列 ──
        //    行2 的执行标准是模板固定文本（现役同样"不覆盖"）；仪器设备三项也是固定文本。
        Row2();
        Row3();
        Row4();
        Row5();
        Row6();
        Row7();
        Row8();

        // ── 分节标题（行 9，合并 A9:I9）──
        workbook.SetCellValue(9, 1, T("一、试件基本物理指标及试验结果", "1. Specimen properties and test results"));
        workbook.MergeCells(9, 1, 9, 9);
        Style(workbook, sectionFormat, 9, 1, 9, 9);

        // ── 明细表头（行 10）──
        string[] headers =
        [
            T("试件编号", "Sample"),
            T("试件长度(mm)", "Length"),
            T("试件宽度(mm)", "Width"),
            T("试件高度(mm)", "Height"),
            T("跨中挠度(mm)", "Deflection"),
            T("破坏时荷载(kN)", "Peak load"),
            T("弯拉强度(MPa)", "Strength"),
            T("最大弯拉应变(με)", "Max strain"),
            T("异常值判断", "Validity"),
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            workbook.SetCellValue(10, i + 1, headers[i]);
        }

        Style(workbook, headerFormat, 10, 1, 10, 9);

        // ── 明细数据区（行 11~25，最多 15 件）：**整块都套好边框** ──
        //    只有第一行放占位符（导出器把第 i 条明细写在"起始行 + i"上），
        //    其余 14 行是空的带框行 —— 这样明细不足 15 件时表格框线依然完整。
        Style(workbook, cellFormat, 11, 1, 25, 9);

        string[] detailLabels =
        [
            "Detail.SampleID",
            "Detail.SampleLength",
            "Detail.SampleWidth",
            "Detail.SampleHeight",
            "Detail.CorrectedDeflection",
            "Detail.PeakLoadValue",
            "Detail.FlexuralStrength",
            "Detail.MaxBendingTensileStrain",
            "Detail.EffectiveText",
        ];
        for (var i = 0; i < detailLabels.Length; i++)
        {
            workbook.SetCellValue(11, i + 1, Label(detailLabels[i]));
        }

        // ── 其他需要说明的情况（标签 A27:B27，值区合并 C27:I30）──
        workbook.SetCellValue(27, 1, T("其他需要说明的情况：", "Other remarks:"));
        workbook.MergeCells(27, 1, 27, 2);
        workbook.MergeCells(27, 3, 30, 9);
        Style(workbook, labelFormat, 27, 1, 27, 2);
        Style(workbook, noteFormat, 27, 3, 30, 9);

        // ── 签名与日期（行 31~32）：标签在 A/D/G，值写在 B/E/H ──
        workbook.SetCellValue(31, 1, T("试验人员：", "Tester:"));
        workbook.SetCellValue(31, 4, T("复核人员：", "Reviewer:"));
        workbook.SetCellValue(31, 7, T("监理人员：", "Supervisor:"));
        workbook.SetCellValue(32, 1, T("日期：", "Date:"));
        workbook.SetCellValue(32, 4, T("日期：", "Date:"));
        workbook.SetCellValue(32, 7, T("日期：", "Date:"));

        foreach (var col in new[] { 1, 4, 7 })
        {
            Style(workbook, labelFormat, 31, col);
            Style(workbook, labelFormat, 32, col);
        }

        // 试验人员取单上的"试验员"；试验日期取该单的"试验时间"（现役这三个位置里，
        // 复核/监理目前没有对应数据源，留空手填）
        workbook.SetCellValue(31, 2, Label("Sheet.Operator"));
        workbook.SetCellValue(32, 2, Label("Sheet.CreatedOn"));
        foreach (var col in new[] { 2, 5, 8 })
        {
            Style(workbook, signValueFormat, 31, col);
            Style(workbook, signValueFormat, 32, col);
        }

        // ── 表尾曲线图区（用户 2026-10-15）──
        //    标记行本身是**图片区标题行**：导出器把曲线图从它的下一行开始排（两张一行、竖向往下），
        //    这次没有曲线图时会把这一行到模板末尾整段删掉 —— 所以不留一个空标题。
        workbook.SetCellValue(34, 1, T("二、试验曲线图", "2. Test curves"));
        workbook.SetCellValue(34, 9, Label("Images"));
        Style(workbook, sectionFormat, 34, 1, 34, 9);

        // ── 出厂版本戳：写在隐藏的 K 列（报表本身只占 A~I），导出前据此判断要不要换代 ──
        FactoryTemplateStore.ApplyStamp(workbook, TemplateVersion);

        // ── 页面设置：照现役实物 ──
        //    现役 **8 份**报表模板（T0709 / 0715 / 0716 / 0744 / 0746 / 0747 / 0767 / 0772）
        //    全部是 `paperSize="9"`（A4）+ `orientation="landscape"` + 水平/垂直居中，
        //    页边距也压得很小（左右 0.236" ≈ 6mm、上下 0.157" ≈ 4mm、页眉页脚 0.118" ≈ 3mm），
        //    且**不缩放**（没有 fitToWidth）。表宽 A~I ≈ 25.6cm，A4 横向可打印宽 ≈ 28.5cm，100% 排得下。
        workbook.PrintPaperSize = TPaperSize.A4;
        workbook.PrintLandscape = true;
        workbook.PrintHCentered = true;
        workbook.PrintVCentered = true;
        workbook.SetPrintMargins(
            new TXlsMargins(
                aLeft: 0.23622047244094491,
                aTop: 0.15748031496062992,
                aRight: 0.23622047244094491,
                aBottom: 0.15748031496062992,
                aHeader: 0.11811023622047245,
                aFooter: 0.11811023622047245
            )
        );

        workbook.Save(path);

        // ── 行 2~8 的填充（写成局部函数，保持代码从上到下的可读顺序）──
        void Row2()
        {
            Header(2, 1, T("试验项目名称：", "Project name:"), 2);
            Header(2, 3, "{{Sheet.ProjectName}}", 2, isValue: true);
            Header(2, 5, T("执行标准：", "Standard:"), 1);
            workbook.SetCellValue(2, 6, T(StandardTextZh, StandardTextEn));
            workbook.MergeCells(2, 6, 2, 9);
            Style(workbook, standardFormat, 2, 6);
        }

        void Row3()
        {
            Header(3, 1, T("委托单位：", "Client:"), 2);
            Header(3, 3, "{{Sheet.ClientUnits}}", 2, isValue: true);
            Header(3, 5, T("试验方法：", "Test method:"), 1);
            workbook.SetCellValue(3, 6, Label("Sheet.TestObjective"));
            workbook.MergeCells(3, 6, 3, 9);
            Style(workbook, valueFormat, 3, 6);
        }

        void Row4()
        {
            Header(4, 1, T("沥青混合料类型：", "Mixture type:"), 2);
            Header(4, 3, "{{Sheet.MixtureType}}", 2, isValue: true);
            Header(4, 5, T("接样日期：", "Sample date:"), 1);
            workbook.SetCellValue(4, 6, Label("Sheet.AcceptanceDate"));
            workbook.MergeCells(4, 6, 4, 7);
            Style(workbook, valueFormat, 4, 6);
            Header(4, 8, T("试验日期：", "Test date:"), 1);
            workbook.SetCellValue(4, 9, Label("Sheet.CreatedOn"));
            Style(workbook, valueFormat, 4, 9);
        }

        void Row5()
        {
            Header(5, 1, T("毛体积密度(g/cm³)：", "Bulk density (g/cm³):"), 2);
            Header(5, 3, "{{Sheet.SampleDensity}}", 2, isValue: true);
            Header(5, 5, T("空隙率(%)：", "Air voids (%):"), 1);
            workbook.SetCellValue(5, 6, Label("Sheet.VoidRatio"));
            workbook.MergeCells(5, 6, 5, 7);
            Style(workbook, valueFormat, 5, 6);
            Header(5, 8, T("沥青用量(%)：", "Asphalt content (%):"), 1);
            workbook.SetCellValue(5, 9, Label("Sheet.AsphaltUsage"));
            Style(workbook, valueFormat, 5, 9);
        }

        void Row6()
        {
            Header(6, 1, T("沥青体积百分率(%)：", "Asphalt volume (%):"), 2);
            Header(6, 3, "{{Sheet.ContentByVolume}}", 2, isValue: true);
            Header(6, 5, T("沥青饱和度(%)：", "Asphalt saturation (%):"), 1);
            workbook.SetCellValue(6, 6, Label("Sheet.AsphaltSaturation"));
            workbook.MergeCells(6, 6, 6, 7);
            Style(workbook, valueFormat, 6, 6);
            Header(6, 8, T("矿料间隙率(%)：", "VMA (%):"), 1);
            workbook.SetCellValue(6, 9, Label("Sheet.MineralAggregateVoidRatio"));
            Style(workbook, valueFormat, 6, 9);
        }

        void Row7()
        {
            Header(7, 1, T("试验温度( ℃)：", "Temperature (℃):"), 2);
            Header(7, 3, "{{Sheet.TestTemperature}}", 2, isValue: true);
            Header(7, 5, T("试件尺寸：", "Specimen size:"), 1);
            workbook.SetCellValue(7, 6, Label("Sheet.SampleSizeDesc"));
            workbook.MergeCells(7, 6, 7, 7);
            Style(workbook, valueFormat, 7, 6);
        }

        void Row8()
        {
            Header(8, 1, T("仪器设备名称：", "Instrument:"), 2);
            Header(8, 3, T(InstrumentNameZh, InstrumentNameEn), 2, isValue: true);
            Header(8, 5, T("型号：", "Model:"), 1);
            workbook.SetCellValue(8, 6, InstrumentModel);
            workbook.MergeCells(8, 6, 8, 7);
            Style(workbook, valueFormat, 8, 6);
            Header(8, 8, T("设备编号：", "Serial no.:"), 1);
            workbook.SetCellValue(8, 9, InstrumentNo);
            Style(workbook, valueFormat, 8, 9);
        }

        /// <summary>
        /// 一行的"标签 + 值"：<paramref name="span"/> = 占几列（**1 = 不合并**）。
        /// 现役模板的合并区是固定的（A2:B2 与 C2:D2 是 2 列、F2:I2 是 4 列、E/H 列标签是单格），
        /// 所以跨度按格填，别一律给同一个数（这里踩过：值区给成 4 列，合并区就跟现役不一样了）。
        /// </summary>
        void Header(int row, int col, string text, int labelSpan, bool isValue = false)
        {
            workbook.SetCellValue(row, col, text);
            var lastCol = col + Math.Max(1, labelSpan) - 1;

            if (lastCol > col)
            {
                workbook.MergeCells(row, col, row, lastCol);
            }

            Style(workbook, isValue ? valueFormat : labelFormat, row, col, row, lastCol);
        }
    }

    /// <summary>`{{标签}}` 占位符（与 <c>FlexCelReportExporter</c> 的写法一致）。</summary>
    private static string Label(string name) => "{{" + name + "}}";

    /// <summary>现役模板的列宽 A~I。</summary>
    private static IEnumerable<(int Col, double Width)> ColumnWidths()
    {
        double[] widths = [18.44, 14.55, 14.66, 12, 16.66, 13, 12.66, 18, 18.11];
        for (var i = 0; i < widths.Length; i++)
        {
            yield return (i + 1, widths[i]);
        }
    }

    /// <summary>给一个区域套格式（逐格调用即可 —— 格子不多，不必用范围重载）。</summary>
    private static void Style(XlsFile workbook, int xf, int row1, int col1, int row2 = -1, int col2 = -1)
    {
        row2 = row2 < 0 ? row1 : row2;
        col2 = col2 < 0 ? col1 : col2;

        for (var row = row1; row <= row2; row++)
        {
            for (var col = col1; col <= col2; col++)
            {
                workbook.SetCellFormat(row, col, xf);
            }
        }
    }

    /// <summary>
    /// 造一个格式并登记进工作簿，返回它的 XF 索引（<c>SetCellFormat</c> 用）。
    ///
    /// 注意 FlexCel 的用法：<c>TFlxFormat</c> **没有公开构造器**，要从
    /// <c>workbook.GetDefaultFormat</c> 拿一份**副本**再改（实测：改副本不影响工作簿默认格式，
    /// 且 <c>AddFormat</c> 会按当时的内容快照，所以每个格式都重新取一次副本）。
    /// </summary>
    private static int AddFormat(
        XlsFile workbook,
        string fontName,
        int sizeInPoints,
        bool bold,
        THFlxAlignment horizontal,
        TVFlxAlignment vertical,
        bool tableBorder = false,
        bool bottomLine = false,
        TExcelColor? fontColor = null,
        TExcelColor? fillColor = null
    )
    {
        var format = workbook.GetDefaultFormat;
        format.Font.Name = fontName;
        format.Font.Size20 = (short)(sizeInPoints * 20);
        format.Font.Style = bold ? TFlxFontStyles.Bold : TFlxFontStyles.None;
        format.HAlignment = horizontal;
        format.VAlignment = vertical;
        format.WrapText = true;

        if (fontColor is not null)
        {
            format.Font.Color = fontColor.Value;
        }

        if (fillColor is not null)
        {
            // 实心填充：`FillPattern` 是 TFlxFormat 的**公共字段**（不是属性，反射按属性名找会找不到）。
            // 实心填充的可见色在 FgColor 上（BgColor 只在图案填充里有意义）。
            format.FillPattern.Pattern = TFlxPatternStyle.Solid;
            format.FillPattern.FgColor = fillColor.Value;
        }

        if (tableBorder)
        {
            var border = new TFlxOneBorder { Style = TFlxBorderStyle.Thin };
            format.Borders = new TFlxBorders
            {
                Top = border,
                Bottom = border,
                Left = border,
                Right = border,
            };
        }
        else if (bottomLine)
        {
            // 签名/日期那种"下面一条横线"（照现役：只有下边框、底端对齐）
            format.Borders = new TFlxBorders
            {
                Bottom = new TFlxOneBorder { Style = TFlxBorderStyle.Thin },
            };
        }

        return workbook.AddFormat(format);
    }
}
