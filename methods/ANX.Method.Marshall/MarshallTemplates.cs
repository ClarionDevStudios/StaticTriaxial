namespace ANX.Method.Marshall;

using ANX.Reporting;
using FlexCel.Core;
using FlexCel.XlsAdapter;

/// <summary>
/// T 0709 的**出厂报表模板**（标签制，中英两套）。
///
/// **版式逐格照现役**（用户 2026-10-14："报表模板按照现役软件的格式改写"）：照抄现役模板
/// `Bins\Report\Template\T0709-2025Template.xlsx` 的单元格、合并区、列宽、行高与格式
/// （把 xlsx 当 zip 读 `sheet1.xml` / `sharedStrings.xml` / `styles.xml` 与 `mergeCells` 量出来的）：
/// <list type="bullet">
///   <item>标题行 R1（A1:I1 合并，微软雅黑 14 白色粗体、底色 #003366、行高 32.25）；</item>
///   <item>报表头 R2~R8（标签在 A/E/H、值在 C/F/I；行高 15）——执行标准与仪器设备三项是模板固定文本；</item>
///   <item>分节标题 R9 / R24（底色 #D9E1F2、行高 24）；明细表头 R10 与汇总表头 R25
///         （白字、底色 #0070C0、行高 33.9）；</item>
///   <item>明细数据行 R11~R22（斑马线；R23 是模板保留的间隔行）、汇总 R26~R28（标准/浸水/真空饱水三组）、
///         说明区（A30:B30 标签 + C30:I33 值区）、签名区 R34~R35（打印后手工签名）。</item>
/// </list>
///
/// 两处**有意差异**（都不改版式）：
/// <list type="number">
///   <item>执行标准按用户 2026-10-15 的裁定改正笔误：现役写 `JTC3410-2025`，我们写 `JTG3410-2025`；</item>
///   <item>表尾加**曲线图区**（R36 间隔 + R37 标题行带 `{{Images}}`，图从 R38 起两张一行往下排）——
///         现役是把曲线 PNG 放在报表同目录、不嵌进表里，本项目按用户要求（2026-10-15）嵌图。</item>
/// </list>
///
/// 明细行数不成问题：模板先排满 12 行带框明细行（照现役），装不下时由导出器**按需插行**
/// （复制块尾那一行的格式），下面的汇总、说明、签名与图片区跟着整体下移。
/// 模板换代（版本戳）、页面设置（A4 横向 + 居中 + 压小页边距）沿用 <see cref="FactoryTemplateStore"/>
/// 与弯曲同一条口径。
/// </summary>
public static class MarshallTemplates
{
    /// <summary>模板文件名的语种后缀。</summary>
    public const string ChineseCulture = "zh-CN";

    public const string EnglishCulture = "en-US";

    /// <summary>
    /// **出厂模板版本号**：改了模板的单元格结构、固定文本或版式就 +1。
    /// 用法与规则见 <see cref="FactoryTemplateStore"/>（落后 → 备份旧的 + 重写出厂模板；一致 → 原样保留）。
    ///
    /// 版本沿革：v1（2026-10-16）首版 —— 版式逐格照现役 T0709 模板，含表尾曲线图区。
    /// </summary>
    public const int TemplateVersion = 1;

    /// <summary>字体：现役整套用**微软雅黑**。</summary>
    private const string TemplateFontName = "微软雅黑";

    /// <summary>主标题底色：现役 A1 是深藏蓝 `#003366` + 14pt 白色粗体。</summary>
    private static readonly TExcelColor TitleFill = TExcelColor.FromArgb(0x00, 0x33, 0x66);

    /// <summary>分节标题底色：现役 A9/A24 是浅蓝 `#D9E1F2`。</summary>
    private static readonly TExcelColor SectionFill = TExcelColor.FromArgb(0xD9, 0xE1, 0xF2);

    /// <summary>表头底色：现役 R10/R25 是蓝 `#0070C0` + 白色粗体。</summary>
    private static readonly TExcelColor HeaderFill = TExcelColor.FromArgb(0x00, 0x70, 0xC0);

    private static readonly TExcelColor White = TExcelColor.FromArgb(0xFF, 0xFF, 0xFF);

    /// <summary>执行标准：现役写 `JTC3410-2025`（笔误），按用户 2026-10-15 的裁定改正为 `JTG3410-2025`。</summary>
    private const string StandardTextZh = "JTG3410-2025 / T0709-2025 沥青混合料马歇尔稳定度试验";

    private const string StandardTextEn =
        "JTG3410-2025 / T0709-2025 Asphalt Mixture Marshall Stability Test";

    /// <summary>仪器设备信息：现役报表代码里写死的常量（现役 `UT0709Report.pas`）。</summary>
    private const string InstrumentNameZh = "马歇尔稳定度测定仪";

    private const string InstrumentNameEn = "Marshall stability tester";

    private const string InstrumentModel = "ZY-64";

    private const string InstrumentNo = "JLMS-2024-001";

    /// <summary>行高换算：FlexCel 的行高单位是 1/20 磅（现役 ht 值就是磅）。</summary>
    private static int RowHeight(double points) => (int)Math.Round(points * 20);

    /// <summary>出厂模板管理（版本戳 / 换代备份 / 按语种确保最新）—— 通用件，各方法共用同一条口径。</summary>
    private static FactoryTemplateStore Store(string templatesRoot) =>
        new(templatesRoot, "Marshall", TemplateVersion, [ChineseCulture, EnglishCulture], Write);

    /// <summary>
    /// 确保两套模板都是**本版出厂模板**；返回中文模板路径。
    /// </summary>
    public static string EnsureDefaultTemplates(string templatesRoot)
    {
        Store(templatesRoot).EnsureAll();

        return Path.Combine(templatesRoot, $"Marshall.{ChineseCulture}.xlsx");
    }

    /// <summary>取指定语种的模板路径（不存在则先写出）。</summary>
    public static string Resolve(string templatesRoot, string culture)
    {
        EnsureDefaultTemplates(templatesRoot);
        var normalized = string.Equals(culture, EnglishCulture, StringComparison.OrdinalIgnoreCase)
            ? EnglishCulture
            : ChineseCulture;
        return Path.Combine(templatesRoot, $"Marshall.{normalized}.xlsx");
    }

    private static void Write(string path, string culture)
    {
        var isEnglish = string.Equals(culture, EnglishCulture, StringComparison.OrdinalIgnoreCase);

        string T(string zh, string en) => isEnglish ? en : zh;

        var workbook = new XlsFile();
        workbook.NewFile();

        // ── 格式：照现役 `T0709-2025Template.xlsx` 的 styles.xml 逐条对出来 ──
        var titleFormat = AddFormat(
            workbook,
            TemplateFontName,
            14,
            bold: true,
            THFlxAlignment.center,
            TVFlxAlignment.center,
            fontColor: White,
            fillColor: TitleFill
        );
        var labelFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: true,
            THFlxAlignment.right,
            TVFlxAlignment.center
        );
        var valueFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: false,
            THFlxAlignment.left,
            TVFlxAlignment.center
        );
        var standardFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: true,
            THFlxAlignment.center,
            TVFlxAlignment.center
        );
        var sectionFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: true,
            THFlxAlignment.left,
            TVFlxAlignment.center,
            fillColor: SectionFill
        );
        var headerFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: true,
            THFlxAlignment.center,
            TVFlxAlignment.center,
            tableBorder: true,
            fontColor: White,
            fillColor: HeaderFill
        );
        var cellCenterFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: false,
            THFlxAlignment.center,
            TVFlxAlignment.center,
            tableBorder: true
        );
        var cellRightFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: false,
            THFlxAlignment.right,
            TVFlxAlignment.center,
            tableBorder: true
        );
        var noteFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: false,
            THFlxAlignment.left,
            TVFlxAlignment.center,
            tableBorder: true
        );
        var signValueFormat = AddFormat(
            workbook,
            TemplateFontName,
            10,
            bold: false,
            THFlxAlignment.general,
            TVFlxAlignment.bottom,
            bottomLine: true
        );

        // ── 列宽（照现役模板实测）──
        foreach (var (col, width) in ColumnWidths())
        {
            workbook.SetColWidth(col, (int)Math.Round(width * 256));
        }

        // ── 行高（照现役：标题 32.25、分节 24、表头 33.9，其余 15）──
        workbook.SetRowHeight(1, RowHeight(32.25));
        for (var row = 2; row <= 8; row++)
        {
            workbook.SetRowHeight(row, RowHeight(15));
        }
        workbook.SetRowHeight(9, RowHeight(24));
        workbook.SetRowHeight(10, RowHeight(33.9));
        for (var row = 11; row <= 23; row++)
        {
            workbook.SetRowHeight(row, RowHeight(15));
        }
        workbook.SetRowHeight(24, RowHeight(24));
        workbook.SetRowHeight(25, RowHeight(33.9));
        for (var row = 26; row <= 36; row++)
        {
            workbook.SetRowHeight(row, RowHeight(15));
        }
        workbook.SetRowHeight(37, RowHeight(24)); // 图片区标题行，与分节标题同高

        // ── 标题（合并 A1:I1）──
        workbook.SetCellValue(1, 1, T("沥青混合料马歇尔稳定度试验报表", "Asphalt Mixture Marshall Stability Test Report"));
        workbook.MergeCells(1, 1, 1, 9);
        Style(workbook, titleFormat, 1, 1, 1, 9);

        // ── 报表头（行 2~8）──
        // R2：试验项目名称 + 执行标准（固定文本，合并 F2:I2）
        Header(2, 1, T("试验项目名称：", "Project:"), 2);
        Header(2, 3, Label("Sheet.ProjectName"), 2, isValue: true);
        Header(2, 5, T("执行标准：", "Standard:"), 1);
        workbook.SetCellValue(2, 6, T(StandardTextZh, StandardTextEn));
        workbook.MergeCells(2, 6, 2, 9);
        Style(workbook, standardFormat, 2, 6, 2, 9);

        // R3：委托单位 + 试验方法（TestStandardsDesc：标准/浸水/真空饱水马歇尔）
        Header(3, 1, T("委托单位：", "Client:"), 2);
        Header(3, 3, Label("Sheet.ClientUnits"), 2, isValue: true);
        Header(3, 5, T("试验方法：", "Method:"), 1);
        workbook.SetCellValue(3, 6, Label("Sheet.TestMethod"));
        workbook.MergeCells(3, 6, 3, 9);
        Style(workbook, valueFormat, 3, 6, 3, 9);

        // R4：沥青混合料类型 + 接样日期 + 试验日期
        Header(4, 1, T("沥青混合料类型：", "Mixture type:"), 2);
        Header(4, 3, Label("Sheet.MixtureType"), 2, isValue: true);
        Header(4, 5, T("接样日期：", "Sample date:"), 1);
        workbook.SetCellValue(4, 6, Label("Sheet.AcceptanceDate"));
        workbook.MergeCells(4, 6, 4, 7);
        Style(workbook, valueFormat, 4, 6, 4, 7);
        Header(4, 8, T("试验日期：", "Test date:"), 1);
        workbook.SetCellValue(4, 9, Label("Sheet.TestDate"));
        Style(workbook, valueFormat, 4, 9);

        // R5：毛体积密度 + 空隙率 + 沥青用量
        Header(5, 1, T("毛体积密度(g/cm³)：", "Bulk density (g/cm³):"), 2);
        Header(5, 3, Label("Sheet.SampleDensity"), 2, isValue: true);
        Header(5, 5, T("空隙率(%)：", "Air voids (%):"), 1);
        workbook.SetCellValue(5, 6, Label("Sheet.VoidRatio"));
        workbook.MergeCells(5, 6, 5, 7);
        Style(workbook, valueFormat, 5, 6, 5, 7);
        Header(5, 8, T("沥青用量(%)：", "Asphalt (%):"), 1);
        workbook.SetCellValue(5, 9, Label("Sheet.AsphaltUsage"));
        Style(workbook, valueFormat, 5, 9);

        // R6：沥青体积百分率 + 沥青饱和度 + 矿料间隙率
        Header(6, 1, T("沥青体积百分率(%)：", "Asphalt vol. (%):"), 2);
        Header(6, 3, Label("Sheet.ContentByVolume"), 2, isValue: true);
        Header(6, 5, T("沥青饱和度(%)：", "Saturation (%):"), 1);
        workbook.SetCellValue(6, 6, Label("Sheet.AsphaltSaturation"));
        workbook.MergeCells(6, 6, 6, 7);
        Style(workbook, valueFormat, 6, 6, 6, 7);
        Header(6, 8, T("矿料间隙率(%)：", "VMA (%):"), 1);
        workbook.SetCellValue(6, 9, Label("Sheet.MineralAggregateVoidRatio"));
        Style(workbook, valueFormat, 6, 9);

        // R7：试验温度 + 试件尺寸
        Header(7, 1, T("试验温度( ℃)：", "Temp. (℃):"), 2);
        Header(7, 3, Label("Sheet.TestTemperature"), 2, isValue: true);
        Header(7, 5, T("试件尺寸：", "Specimen size:"), 1);
        workbook.SetCellValue(7, 6, Label("Sheet.SampleSizeDesc"));
        workbook.MergeCells(7, 6, 7, 7);
        Style(workbook, valueFormat, 7, 6, 7, 7);

        // R8：仪器设备（固定文本）
        Header(8, 1, T("仪器设备名称：", "Instrument:"), 2);
        Header(8, 3, T(InstrumentNameZh, InstrumentNameEn), 2, isValue: true);
        Header(8, 5, T("型号：", "Model:"), 1);
        workbook.SetCellValue(8, 6, InstrumentModel);
        workbook.MergeCells(8, 6, 8, 7);
        Style(workbook, valueFormat, 8, 6, 8, 7);
        Header(8, 8, T("设备编号：", "Serial no.:"), 1);
        workbook.SetCellValue(8, 9, InstrumentNo);
        Style(workbook, valueFormat, 8, 9);

        // ── 分节标题（行 9，合并 A9:I9）──
        workbook.SetCellValue(9, 1, T("一、试件基本物理指标及试验结果", "1. Specimen properties and test results"));
        workbook.MergeCells(9, 1, 9, 9);
        Style(workbook, sectionFormat, 9, 1, 9, 9);

        // ── 明细表头（行 10）──
        string[] headers =
        [
            T("试件编号", "Sample"),
            T("试件直径(mm)", "Dia. (mm)"),
            T("试件高度(mm)", "Height (mm)"),
            T("试验类型", "Test type"),
            T("稳定度MS(kN)", "Stability (kN)"),
            T("流值FL(mm)", "Flow (mm)"),
            T("马歇尔模数T(kN/mm)", "Modulus (kN/mm)"),
            T("异常值判断", "Validity"),
            T("备注", "Remark"),
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            workbook.SetCellValue(10, i + 1, headers[i]);
        }

        Style(workbook, headerFormat, 10, 1, 10, 9);

        // ── 明细数据区（行 11~22，12 行）：**整块套好边框**，只有首行放占位符 ──
        Style(workbook, cellCenterFormat, 11, 1, 22, 2);
        Style(workbook, cellRightFormat, 11, 3, 22, 9);

        string[] detailLabels =
        [
            "Detail.SampleId",
            "Detail.SampleDiameter",
            "Detail.SampleHeight",
            "Detail.TestType",
            "Detail.Stability",
            "Detail.FlowValue",
            "Detail.MarshallModulus",
            "Detail.EffectiveText",
            "Detail.Remark",
        ];
        for (var i = 0; i < detailLabels.Length; i++)
        {
            workbook.SetCellValue(11, i + 1, Label(detailLabels[i]));
        }

        // R23 是模板保留的间隔行（现役同款），不套边框 —— 导出器据此判断明细块到此为止

        // ── 汇总区（行 24 分节、行 25 表头、行 26~28 三组）──
        workbook.SetCellValue(24, 1, T("二、试验结果汇总与计算", "2. Summary and calculation"));
        workbook.MergeCells(24, 1, 24, 9);
        Style(workbook, sectionFormat, 24, 1, 24, 9);

        string[] summaryHeaders =
        [
            T("统计项目", "Item"),
            T("稳定度平均值(kN)", "Avg. stability (kN)"),
            T("稳定度标准差", "Stability SD"),
            T("稳定度变异系数(%)", "Stability CV (%)"),
            T("流值平均值(mm)", "Avg. flow (mm)"),
            T("流值标准差", "Flow SD"),
            T("流值变异系数(%)", "Flow CV (%)"),
            T("浸水稳定度比(%)", "Soaked ratio (%)"),
            T("真空饱水稳定度比(%)", "Vacuum ratio (%)"),
        ];
        for (var i = 0; i < summaryHeaders.Length; i++)
        {
            workbook.SetCellValue(25, i + 1, summaryHeaders[i]);
        }

        Style(workbook, headerFormat, 25, 1, 25, 9);

        string[] groupLabels =
        [
            T("标准马歇尔组", "Standard Marshall"),
            T("浸水马歇尔组", "Soaked Marshall"),
            T("真空饱水马歇尔组", "Vacuum-saturated Marshall"),
        ];
        string[] groupColumns =
        [
            "Group1AvgStability",
            "Group1StabilityStdDev",
            "Group1StabilityCvPercent",
            "Group1AvgFlowValue",
            "Group1FlowStdDev",
            "Group1FlowCvPercent",
        ];
        string[] group2Columns =
        [
            "Group2AvgStability",
            "Group2StabilityStdDev",
            "Group2StabilityCvPercent",
            "Group2AvgFlowValue",
            "Group2FlowStdDev",
            "Group2FlowCvPercent",
        ];

        for (var group = 0; group < groupLabels.Length; group++)
        {
            var row = 26 + group;
            workbook.SetCellValue(row, 1, groupLabels[group]);
            Style(workbook, cellCenterFormat, row, 1);

            var columns = group == 0 ? groupColumns : group2Columns;
            for (var i = 0; i < columns.Length; i++)
            {
                workbook.SetCellValue(row, i + 2, Label("Result." + columns[i]));
                Style(workbook, cellRightFormat, row, i + 2);
            }

            // 稳定度比只落在对应组那一格（现役：H27 = 浸水、I28 = 真空饱水）
            if (group == 1)
            {
                workbook.SetCellValue(row, 8, Label("Result.ImmersionRatio"));
            }
            else if (group == 2)
            {
                workbook.SetCellValue(row, 9, Label("Result.VacuumRatio"));
            }

            Style(workbook, cellRightFormat, row, 8, row, 9);
        }

        // ── 其他需要说明的情况（标签 A30:B30，值区合并 C30:I33）──
        //  值区留空（没有数据源；现役模板里那两格是示例文字，我们不抄示例数据）。
        workbook.SetCellValue(30, 1, T("其他需要说明的情况：", "Other remarks:"));
        workbook.MergeCells(30, 1, 30, 2);
        workbook.MergeCells(30, 3, 33, 9);
        Style(workbook, labelFormat, 30, 1, 30, 2);
        Style(workbook, noteFormat, 30, 3, 33, 9);

        // ── 签名与日期（行 34~35）：打印后手工签名；试验人员与日期预填 ──
        workbook.SetCellValue(34, 1, T("试验人员：", "Tester:"));
        workbook.SetCellValue(34, 4, T("复核人员：", "Reviewer:"));
        workbook.SetCellValue(34, 7, T("监理人员：", "Supervisor:"));
        workbook.SetCellValue(35, 1, T("日期：", "Date:"));
        workbook.SetCellValue(35, 4, T("日期：", "Date:"));
        workbook.SetCellValue(35, 7, T("日期：", "Date:"));
        workbook.MergeCells(35, 8, 35, 9);

        foreach (var col in new[] { 1, 4, 7 })
        {
            Style(workbook, labelFormat, 34, col);
            Style(workbook, labelFormat, 35, col);
        }

        workbook.SetCellValue(34, 2, Label("Sheet.Tester"));
        workbook.SetCellValue(35, 2, Label("Sheet.TestDate"));
        foreach (var col in new[] { 2, 5, 8 })
        {
            Style(workbook, signValueFormat, 34, col);
            Style(workbook, signValueFormat, 35, col);
        }

        // ── 表尾曲线图区（用户 2026-10-15：分 2 列、竖向排列）──
        //  R37 是**图片区标题行**：导出器把曲线图从下一行开始排；这次没有图时
        //  会把这一行到模板末尾整段删掉（连标题一起），不留空标题。
        workbook.SetCellValue(37, 1, T("三、试验曲线图", "3. Test curves"));
        workbook.SetCellValue(37, 9, Label("Images"));
        Style(workbook, sectionFormat, 37, 1, 37, 9);

        // ── 出厂版本戳：写在隐藏的 K 列（报表本身只占 A~I）──
        FactoryTemplateStore.ApplyStamp(workbook, TemplateVersion);

        // J 列隐藏成隔离列：版本戳占了 K 列，不隐藏的话打印宽度会一直算到 K（A~I + J > A4 横向可打印宽）
        workbook.SetColHidden(FactoryTemplateStore.StampColumn - 1, true);

        // ── 页面设置：照现役实物（A4 横向 + 水平/垂直居中 + 压小的页边距）──
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

        /// <summary>
        /// 一行的"标签 + 值"：<paramref name="span"/> = 占几列（**1 = 不合并**）。
        /// 现役模板的合并区是固定的（A2:B2 与 C2:D2 是 2 列、F2:I2 是 4 列、E/H 列标签是单格），
        /// 所以跨度按格填。
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
        double[] widths =
        [
            17.109375,
            12.88671875,
            13,
            19.109375,
            17,
            11.5546875,
            15.109375,
            16.5546875,
            19.109375,
        ];
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
    /// FlexCel 的用法要点见 <c>BendingTemplates.AddFormat</c> 的注释（TFlxFormat 没有公开构造器，
    /// 要从 <c>GetDefaultFormat</c> 取副本再改；实心填充写在 <c>FillPattern</c> 的 FgColor 上）。
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
            format.Borders = new TFlxBorders
            {
                Bottom = new TFlxOneBorder { Style = TFlxBorderStyle.Thin },
            };
        }

        return workbook.AddFormat(format);
    }
}
