namespace STS.Method.ShearStrength;

using STS.Reporting;
using FlexCel.Core;
using FlexCel.XlsAdapter;

/// <summary>
/// T 1236 的**出厂报表模板**（标签制，中英两套），版式按报告 6.1~6.8 立栏。
///
/// 版面（正文 A~L 共 12 列，宽表）：
/// 标题 → 报表头 6.1~6.6（项目名称 / 执行标准 / 样品编号·名称·产地·规格 / 接样日期·样品描述 /
/// 试验日期·缩分方法 / 仪器设备 / 成型方法与参数）→ 分节「一、试验结果」+ 12 列明细表
/// （试件编号 · σ3 · 尺寸 · 质量湿度 · Pmax · ε1 · σ1max · 15% 规则）
/// → 分级平均（5.4，3 级固定行）→ 抗剪强度参数（5.5：c、φ、r、可信）+ SSR（必要时）
/// → 分节「二、要说明的其他内容」（6.8）→ 签名行 → 分节「三、试验曲线图」+ `{{Images}}` 图片区。
///
/// 与 <c>FlexCelReportExporter</c> 的约定：值/明细/图片都写 `{{标签}}` 占位符，导出器按标签填、
/// 不会碰版式；明细块（R11~R25 共 15 行带框）装不下由导出器**按需插行**，下面的分区跟着下移。
///
/// **模板换代**：文件里藏版本戳（<see cref="FactoryTemplateStore"/>，第 1 行 T 列、隐藏），
/// 版本号落后就"备份旧的 + 重写出厂模板"；站点改过的当前版模板不会被覆盖。
/// 正文只到 L 列，M~T 是**隐藏隔离带**（戳列在 T）—— 打印宽度只算 A~L。
/// </summary>
public static class ShearStrengthTemplates
{
    /// <summary>模板文件名的语种后缀。</summary>
    public const string ChineseCulture = "zh-CN";

    public const string EnglishCulture = "en-US";

    /// <summary>
    /// **出厂模板版本号**：改了模板的单元格结构、固定文本或版式就得 +1。
    /// 版本沿革：
    /// <list type="bullet">
    ///   <item>v1（2026-10-16）：首版 —— 按报告 6.1~6.8 立栏（12 列明细表 + 分级平均 + c/φ/r + 曲线图区）。</item>
    ///   <item>v2（2026-10-16，样例预览自检）：修正四处换行被裁 —— 成型主要参数标签加宽、
    ///         参数区（c/φ/r/可信）各标签与值按跨度重排、签名行日期值给两列跨度、曲线图分节标题合并到 K 列。</item>
    ///   <item>v3（2026-10-16，样例预览自检第二轮）：成型行加高（成型参数可能是两行文字）、
    ///         分级平均的数据行补合并（原来只有表头是合并的，数据行渲染成一格一格的空框）。</item>
    /// </list>
    /// </summary>
    public const int TemplateVersion = 3;

    /// <summary>正文列数（A~L）；版本戳在 T 列（<see cref="FactoryTemplateStore.StampColumn"/>）。</summary>
    private const int BodyLastColumn = 12;

    private static FactoryTemplateStore Store(string templatesRoot) =>
        new(
            templatesRoot,
            "ShearStrength",
            TemplateVersion,
            [ChineseCulture, EnglishCulture],
            Write
        );

    /// <summary>
    /// 确保两套模板都是**本版出厂模板**（缺 → 写、旧 → 备份后重写、本版 → 留着）；返回中文模板路径。
    /// </summary>
    public static string EnsureDefaultTemplates(string templatesRoot)
    {
        Store(templatesRoot).EnsureAll();

        return Path.Combine(templatesRoot, $"ShearStrength.{ChineseCulture}.xlsx");
    }

    /// <summary>取指定语种的模板路径（不存在则先写出；语种不认识回落到中文）。</summary>
    public static string Resolve(string templatesRoot, string culture)
    {
        EnsureDefaultTemplates(templatesRoot);
        var normalized = string.Equals(culture, EnglishCulture, StringComparison.OrdinalIgnoreCase)
            ? EnglishCulture
            : ChineseCulture;
        return Path.Combine(templatesRoot, $"ShearStrength.{normalized}.xlsx");
    }

    /// <summary>字体：与现役报表同一支（微软雅黑；英文也用它，两套观感一致）。</summary>
    private const string TemplateFontName = "微软雅黑";

    private static readonly TExcelColor TitleFill = TExcelColor.FromArgb(0x00, 0x33, 0x66);
    private static readonly TExcelColor SectionFill = TExcelColor.FromArgb(0xD9, 0xE1, 0xF2);
    private static readonly TExcelColor HeaderFill = TExcelColor.FromArgb(0x00, 0x70, 0xC0);
    private static readonly TExcelColor White = TExcelColor.FromArgb(0xFF, 0xFF, 0xFF);

    /// <summary>行高换算：FlexCel 的行高单位是 1/20 磅。</summary>
    private static int RowHeight(double points) => (int)Math.Round(points * 20);

    /// <summary>6.1 的"执行标准"固定文本（模板里逐语种写死）。</summary>
    private const string StandardTextZh = "JTG 3442—2026 / T 1236—2026 抗剪强度试验（静三轴法）";

    private const string StandardTextEn = "JTG 3442-2026 / T 1236-2026 Shear Strength Test (Static Triaxial)";

    private static void Write(string path, string culture)
    {
        var isEnglish = string.Equals(culture, EnglishCulture, StringComparison.OrdinalIgnoreCase);

        string T(string zh, string en) => isEnglish ? en : zh;

        var workbook = new XlsFile();
        workbook.NewFile();

        // ── 格式 ──
        var titleFormat = AddFormat(
            workbook, TemplateFontName, 16, bold: true,
            THFlxAlignment.center, TVFlxAlignment.center,
            fontColor: White, fillColor: TitleFill
        );
        var labelFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: true,
            THFlxAlignment.right, TVFlxAlignment.center
        );
        var valueFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: false,
            THFlxAlignment.left, TVFlxAlignment.center
        );
        var standardFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: true,
            THFlxAlignment.left, TVFlxAlignment.center
        );
        var sectionFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: true,
            THFlxAlignment.left, TVFlxAlignment.center,
            fillColor: SectionFill
        );
        var headerFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: true,
            THFlxAlignment.center, TVFlxAlignment.center,
            tableBorder: true, fontColor: White, fillColor: HeaderFill
        );
        var cellFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: false,
            THFlxAlignment.center, TVFlxAlignment.center,
            tableBorder: true
        );
        var noteFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: false,
            THFlxAlignment.left, TVFlxAlignment.top,
            tableBorder: true
        );
        var signValueFormat = AddFormat(
            workbook, TemplateFontName, 11, bold: false,
            THFlxAlignment.general, TVFlxAlignment.bottom,
            bottomLine: true
        );

        // ── 列宽（A~L；合计 ≈ 22cm，A4 横向排得下）──
        double[] widths = [12, 12, 12, 12, 12, 11, 12, 12, 14, 11, 14, 11];
        for (var i = 0; i < widths.Length; i++)
        {
            workbook.SetColWidth(i + 1, (int)Math.Round(widths[i] * 256));
        }

        // ── 行高 ──
        workbook.SetRowHeight(1, RowHeight(38.1)); // 标题
        for (var row = 2; row <= 8; row++)
        {
            workbook.SetRowHeight(row, RowHeight(15));
        }
        workbook.SetRowHeight(9, RowHeight(24)); // 一、试验结果
        workbook.SetRowHeight(8, RowHeight(30)); // 成型行：成型参数可能是两行文字
        workbook.SetRowHeight(10, RowHeight(33.9)); // 明细表头
        for (var row = 11; row <= 30; row++)
        {
            workbook.SetRowHeight(row, RowHeight(15));
        }
        workbook.SetRowHeight(26, RowHeight(24)); // 分级平均分节
        workbook.SetRowHeight(31, RowHeight(24)); // 参数分节
        workbook.SetRowHeight(34, RowHeight(24)); // 6.8 分节
        workbook.SetRowHeight(39, RowHeight(24)); // 曲线图分节

        // ── 标题 ──
        workbook.SetCellValue(1, 1, T("抗剪强度试验（静三轴法）报表", "Shear Strength Test (Static Triaxial) Report"));
        workbook.MergeCells(1, 1, 1, BodyLastColumn);
        Style(workbook, titleFormat, 1, 1, 1, BodyLastColumn);

        // ── 报表头 6.1~6.6（行 2~8）──
        Header(2, 1, T("试验项目名称：", "Project name:"), 2);
        Header(2, 3, "{{Sheet.ProjectName}}", 4, isValue: true);
        Header(2, 7, T("执行标准：", "Standard:"), 1);
        workbook.SetCellValue(2, 8, T(StandardTextZh, StandardTextEn));
        workbook.MergeCells(2, 8, 2, BodyLastColumn);
        Style(workbook, standardFormat, 2, 8);

        Header(3, 1, T("样品编号：", "Sample no.:"), 2);
        Header(3, 3, "{{Sheet.SampleNumber}}", 2, isValue: true);
        Header(3, 5, T("样品名称：", "Sample name:"), 1);
        Header(3, 6, "{{Sheet.SampleName}}", 2, isValue: true);
        Header(3, 8, T("规格：", "Specification:"), 1);
        Header(3, 9, "{{Sheet.SampleSpecification}}", 4, isValue: true);

        Header(4, 1, T("样品产地：", "Origin:"), 2);
        Header(4, 3, "{{Sheet.SampleOrigin}}", 4, isValue: true);
        Header(4, 7, T("缩分方法：", "Reduction:"), 1);
        Header(4, 8, "{{Sheet.SampleReductionMethod}}", 5, isValue: true);

        Header(5, 1, T("接样日期：", "Sample date:"), 2);
        Header(5, 3, "{{Sheet.SampleReceiptDate}}", 4, isValue: true);
        Header(5, 7, T("试验日期：", "Test date:"), 1);
        Header(5, 8, "{{Sheet.CreatedOnDate}}", 5, isValue: true);

        Header(6, 1, T("样品描述：", "Sample description:"), 2);
        Header(6, 3, "{{Sheet.SampleDescription}}", 10, isValue: true);

        Header(7, 1, T("仪器设备名称、型号及编号：", "Instrument (name / model / serial):"), 3);
        Header(7, 4, "{{Sheet.MachineNameAndID}}", 9, isValue: true);

        Header(8, 1, T("成型方法：", "Molding method:"), 2);
        Header(8, 3, "{{Sheet.MoldingMethod}}", 4, isValue: true);
        Header(8, 7, T("成型主要参数：", "Molding parameters:"), 2);
        Header(8, 9, "{{Sheet.MoldingParameters}}", 4, isValue: true);

        // ── 一、试验结果（6.7）──
        workbook.SetCellValue(9, 1, T("一、试验结果", "1. Test results"));
        workbook.MergeCells(9, 1, 9, BodyLastColumn);
        Style(workbook, sectionFormat, 9, 1, 9, BodyLastColumn);

        string[] headers =
        [
            T("试件编号", "Sample"),
            T("试验围压σ3(kPa)", "σ3 (kPa)"),
            T("初始高度(mm)", "Height (mm)"),
            T("初始直径(mm)", "Diameter (mm)"),
            T("试件总质量(g)", "Mass (g)"),
            T("含水率(%)", "w (%)"),
            T("干密度(g/cm³)", "Dry density"),
            T("含水率损失(%)", "w loss (%)"),
            T("最大轴向荷载Pmax(N)", "Pmax (N)"),
            T("破坏应变ε1(%)", "ε1 (%)"),
            T("剪切强度σ1max(kPa)", "σ1max (kPa)"),
            T("15%应变规则", "15% rule"),
        ];
        for (var i = 0; i < headers.Length; i++)
        {
            workbook.SetCellValue(10, i + 1, headers[i]);
        }

        Style(workbook, headerFormat, 10, 1, 10, BodyLastColumn);

        // 明细块 R11~R25（15 行带框；只有第一行放占位符，其余是空的带框行）
        Style(workbook, cellFormat, 11, 1, 25, BodyLastColumn);

        string[] detailLabels =
        [
            "Detail.SampleID",
            "Detail.ConfiningPressure",
            "Detail.InitialHeight",
            "Detail.InitialDiameter",
            "Detail.Mass",
            "Detail.WaterContent",
            "Detail.DryDensity",
            "Detail.WaterContentLoss",
            "Detail.PeakLoadN",
            "Detail.AxialStrain",
            "Detail.ShearStrength",
            "Detail.Strain15Text",
        ];
        for (var i = 0; i < detailLabels.Length; i++)
        {
            workbook.SetCellValue(11, i + 1, Label(detailLabels[i]));
        }

        // ── 分级平均剪切强度（规范 5.4；三级固定行，级数超出本期不展示）──
        workbook.SetCellValue(26, 1, T("分级平均剪切强度（规范 5.4）", "Mean shear strength per level (5.4)"));
        workbook.MergeCells(26, 1, 26, BodyLastColumn);
        Style(workbook, sectionFormat, 26, 1, 26, BodyLastColumn);

        Header(27, 1, T("围压 σ3（kPa）", "σ3 (kPa)"), 4, header: true);
        Header(27, 5, T("平均剪切强度 σ1max（kPa）", "Mean σ1max (kPa)"), 4, header: true);
        Header(27, 9, T("试件数", "Specimens"), 4, header: true);
        for (var level = 1; level <= 3; level++)
        {
            var row = 27 + level;
            workbook.MergeCells(row, 1, row, 4);
            workbook.MergeCells(row, 5, row, 8);
            workbook.MergeCells(row, 9, row, 12);
            Style(workbook, cellFormat, row, 1, row, BodyLastColumn);
            workbook.SetCellValue(row, 1, Label($"Result.Level{level}Sigma3"));
            workbook.SetCellValue(row, 5, Label($"Result.Level{level}Mean"));
            workbook.SetCellValue(row, 9, Label($"Result.Level{level}Count"));
        }

        // ── 抗剪强度参数（规范 5.5）与 SSR（必要时）──
        workbook.SetCellValue(31, 1, T("抗剪强度参数（规范 5.5）", "Shear strength parameters (5.5)"));
        workbook.MergeCells(31, 1, 31, BodyLastColumn);
        Style(workbook, sectionFormat, 31, 1, 31, BodyLastColumn);

        Header(32, 1, T("黏聚力 c（kPa）：", "Cohesion c (kPa):"), 2);
        Header(32, 3, "{{Result.CohesionKPa}}", 2, isValue: true);
        Header(32, 5, T("内摩擦角 φ（°）：", "Friction angle φ (°):"), 2);
        Header(32, 7, "{{Result.FrictionAngleDeg}}", 1, isValue: true);
        Header(32, 8, T("相关系数 r：", "Correlation r:"), 2);
        Header(32, 10, "{{Result.Correlation}}", 1, isValue: true);
        Header(32, 11, T("可信：", "Reliable:"), 1);
        Header(32, 12, "{{Result.ReliableText}}", 1, isValue: true);

        Header(33, 1, T("抗剪强度比 SSR（必要时）：", "SSR (if required):"), 3);
        Header(33, 4, "{{Result.Ssr}}", 2, isValue: true);
        Header(33, 6, T("（按路面实际应力条件计算，规范 5.6 / 5.7）", "(per actual pavement stress conditions, 5.6 / 5.7)"), 7);

        // ── 二、要说明的其他内容（6.8）：三行带框的留白区（可打印后手写；有内容时由字段填）──
        workbook.SetCellValue(34, 1, T("二、要说明的其他内容（规范 6.8）", "2. Other remarks (6.8)"));
        workbook.MergeCells(34, 1, 34, BodyLastColumn);
        Style(workbook, sectionFormat, 34, 1, 34, BodyLastColumn);

        workbook.MergeCells(35, 1, 37, BodyLastColumn);
        Style(workbook, noteFormat, 35, 1, 37, BodyLastColumn);
        workbook.SetCellValue(35, 1, "{{Sheet.Note}}");

        // ── 签名行 ──
        Header(38, 1, T("试验人员：", "Tester:"), 2);
        Header(38, 3, "{{Sheet.TestPerson}}", 2, sign: true);
        Header(38, 5, T("计算人员：", "Calculated by:"), 1);
        Header(38, 6, "{{Sheet.CalculatePerson}}", 2, sign: true);
        Header(38, 8, T("校核人员：", "Checked by:"), 1);
        Header(38, 9, "{{Sheet.VerificationPerson}}", 1, sign: true);
        Header(38, 10, T("日期：", "Date:"), 1);
        Header(38, 11, "{{Sheet.Date}}", 2, sign: true);

        // ── 三、试验曲线图（图片区标记行；导出器把图从下一行起排，没有图时整段删掉）──
        workbook.SetCellValue(39, 1, T("三、试验曲线图", "3. Test curves"));
        workbook.MergeCells(39, 1, 39, BodyLastColumn - 1); // 标题合并到 K，L 列留给 {{Images}} 标记
        workbook.SetCellValue(39, BodyLastColumn, Label("Images"));
        Style(workbook, sectionFormat, 39, 1, 39, BodyLastColumn);

        // ── 版本戳（T 列，隐藏）+ 隔离带（M~T 隐藏：打印宽度只算 A~L）──
        FactoryTemplateStore.ApplyStamp(workbook, TemplateVersion);
        for (var col = BodyLastColumn + 1; col <= FactoryTemplateStore.StampColumn; col++)
        {
            workbook.SetColHidden(col, true);
        }

        // ── 页面设置（照现役：A4 横向 + 居中 + 小边距，不缩放）──
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
        /// 一行的"标签 + 值"：<paramref name="span"/> = 占几列（1 = 不合并）。
        /// <paramref name="header"/> = 表头行（蓝底白字带框）；<paramref name="sign"/> = 签名值（下划线样式）。
        /// </summary>
        void Header(
            int row,
            int col,
            string text,
            int span,
            bool isValue = false,
            bool header = false,
            bool sign = false
        )
        {
            workbook.SetCellValue(row, col, text);
            var lastCol = col + Math.Max(1, span) - 1;

            if (lastCol > col)
            {
                workbook.MergeCells(row, col, row, lastCol);
            }

            var format = header
                ? headerFormat
                : sign
                    ? signValueFormat
                    : isValue
                        ? valueFormat
                        : labelFormat;

            Style(workbook, format, row, col, row, lastCol);
        }
    }

    /// <summary>`{{标签}}` 占位符（与 <c>FlexCelReportExporter</c> 的写法一致）。</summary>
    private static string Label(string name) => "{{" + name + "}}";

    /// <summary>给一个区域套格式（逐格调用即可）。</summary>
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
    /// 造一个格式并登记进工作簿，返回它的 XF 索引。
    /// 注意 FlexCel 的用法：<c>TFlxFormat</c> **没有公开构造器**，要从 <c>workbook.GetDefaultFormat</c>
    /// 拿一份**副本**再改（<c>AddFormat</c> 会按当时内容快照，所以每个格式都重新取一次副本）。
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
