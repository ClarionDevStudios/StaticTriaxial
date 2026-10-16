namespace STS.Ui.ViewModels;

using STS.Control;
using STS.Core;
using STS.Core.TestSheets;
using STS.Ui.Services;

/// <summary>
/// 试件行：**试件号** + 显示标题 + 字段表（都是"当前语言的列名 → 文本"，界面不认识具体含义
/// —— 浏览器给的是资源键，见 <see cref="RebuildSamples"/>）。
///
/// 试件号单独拿出来，是因为曲线是按 <c>单号-试件号</c> 存的（现按"单号 + 试件号"两个条件定位）。
/// 之前从显示标题里 <c>Split(' ')[0]</c> 取，取到的是**列名**（"SampleID"）而不是值 ——
/// 于是历史页的曲线一直读不到（回放图总是空的）。2026-10-13 核对回放标注时发现并修掉。
///
/// 列名一度是库里的列名（<c>SampleID</c> / <c>PeakLoadValue</c>…）直接显示，用户 2026-10-15 要求
/// "和当前语言一致" —— 现在由方法工程给资源键、界面取当前语言。
/// </summary>
public sealed record SampleRow(
    string SampleId,
    string Title,
    IReadOnlyList<KeyValuePair<string, string>> Fields
);

/// <summary>
/// 历史数据页 ViewModel：试验单列表（主）→ 试件列表（从）→ 曲线预览 → 导出报表。
/// 报表导出入口就在这里（与"执行页的打印"共用同一套实现）。
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
    private readonly ITestSheetBrowser _browser;
    private readonly ITestExecution _execution;
    private readonly PrintPreviewViewModel? _preview;
    private readonly IModalPageService? _modalPages;

    /// <summary>
    /// 最近一次读到的试件行（键 = 列名的**显示资源键**）。换语言时按它重算标题与字段标签 ——
    /// 不再读一次库，也不动用户选中的那一件。
    /// </summary>
    private IReadOnlyList<IReadOnlyDictionary<string, string>> _sampleRows = [];

    /// <summary>当前这些试件行属于哪张单（重算标题时要用单号兜底）。</summary>
    private string _sampleSheetId = string.Empty;

    public HistoryViewModel(
        ILocalizationService localization,
        ITestSheetBrowser browser,
        ITestExecution execution,
        PrintPreviewViewModel? preview = null,
        IModalPageService? modalPages = null
    )
        : base(localization)
    {
        _browser = browser;
        _execution = execution;
        _preview = preview;
        _modalPages = modalPages;

        // 换语言：试件列表的列名（原来是库里的 SampleID 这种）跟着换，选中的那一件不动
        localization.LanguageChanged += (_, _) => RebuildSamples();

        SetStatus(L("History.Ready"));
    }

    /// <summary>试验单列表。</summary>
    public ObservableCollection<TestSheetSummary> Sheets { get; } = [];

    /// <summary>选中试验单的试件列表。</summary>
    public ObservableCollection<SampleRow> Samples { get; } = [];

    [ObservableProperty]
    private string _keyword = string.Empty;

    [ObservableProperty]
    private TestSheetSummary? _selectedSheet;

    [ObservableProperty]
    private SampleRow? _selectedSample;

    /// <summary>曲线加载完成（页面据此重绘）。</summary>
    public event EventHandler<IReadOnlyList<CurvePoint>>? CurveLoaded;

    /// <summary>
    /// 当前选中试件的**曲线标注**（修正直线 / 修正原点 / 拟合区间两端）。没有拟合时为 null。
    /// 页面在 <see cref="CurveLoaded"/> 处理里取它 —— 本属性**先于**事件赋值，页面读到的一定是本条曲线的。
    /// </summary>
    public CurveAnnotation? SelectedAnnotation { get; private set; }

    /// <summary>曲线横轴标题（按当前语言；语言切换后由页面重新取一次）。</summary>
    public string CurveXAxisTitle => L(_browser.CurveXAxisKey);

    /// <summary>曲线纵轴标题（按当前语言）。</summary>
    public string CurveYAxisTitle => L(_browser.CurveYAxisKey);

    /// <summary>按关键字查询试验单。</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        Sheets.Clear();
        Samples.Clear();
        SelectedAnnotation = null;

        var sheets = await _browser.QueryAsync(
            string.IsNullOrWhiteSpace(Keyword) ? null : Keyword
        );
        foreach (var sheet in sheets)
        {
            Sheets.Add(sheet);
        }

        SetStatus($"{L("History.Found")} {Sheets.Count} {L("History.SheetsUnit")}");
        SelectedSheet = Sheets.FirstOrDefault();
    }

    /// <summary>
    /// **进页面就自动加载**列表（用户 2026-10-13：不要"必须先点查询"才出数据）。
    /// 与试验单页同口径（<c>SheetSelectionViewModel.OnNavigatedTo</c>；现役 `FormCreate` 也是打开即 `Open`）。
    /// 「查询」按钮保留，只当手动按关键字再查一次。
    /// </summary>
    public override void OnNavigatedTo() => _ = SearchAsync();

    /// <summary>弹窗尺寸：预览要装下一整张表（按宽度铺满），比试样编辑页宽高都大一圈。</summary>
    private const double PreviewDialogWidth = 1120;

    private const double PreviewDialogHeight = 900;

    /// <summary>
    /// 打开**打印预览**（用户 2026-10-15）：报表在预览窗体打开时按**当前界面语言**生成、同名静默覆盖，
    /// 窗体里能看、能打印、能定位文件 —— 原先的"导出报表（中文）/（英文）"两个按钮合并成这一个。
    /// </summary>
    [RelayCommand]
    private void PrintPreview()
    {
        if (SelectedSheet is not { } sheet)
        {
            SetStatus(L("History.SelectSheetFirst"), isError: true);
            return;
        }

        if (_modalPages is null || _preview is null)
        {
            SetStatus(L("Preview.Unavailable"), isError: true);
            return;
        }

        _preview.PrepareFor(sheet.SheetId);
        _modalPages.Show(
            typeof(Views.Pages.PrintPreviewPage),
            PreviewDialogWidth,
            PreviewDialogHeight
        );
    }

    /// <summary>
    /// **按当前参数重算**选中试验单的历史曲线（覆盖已存结果列与组统计）。
    /// 用户 2026-10-12 要求：改了计算期参数（跨径 / 试件尺寸 / 是否原点修正…）后，
    /// 让已存的试验数据按新参数重算，不必重做试验。
    /// </summary>
    [RelayCommand]
    private async Task RecomputeAsync()
    {
        if (SelectedSheet is not { } sheet)
        {
            SetStatus(L("History.SelectSheetFirst"), isError: true);
            return;
        }

        var confirmed = await AppDialogs.ConfirmAsync(
            L("History.Recompute"),
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                L("History.RecomputeConfirm"),
                sheet.SheetId
            ),
            L("Common.Yes"),
            L("Common.No")
        );

        if (!confirmed)
        {
            return;
        }

        try
        {
            var report = await _execution.RecomputeSheetAsync(sheet.SheetId);
            SetStatus(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                L("History.RecomputeDone"),
                report.Recomputed,
                report.Skipped
            ));

            await LoadSamplesAsync(sheet); // 刷新明细，马上能看到新结果
        }
        catch (Exception ex)
        {
            SetStatus($"{L("History.RecomputeFailed")}: {Text(ex)}", isError: true);
        }
    }

    partial void OnSelectedSheetChanged(TestSheetSummary? value)
    {
        _ = LoadSamplesAsync(value);
    }

    partial void OnSelectedSampleChanged(SampleRow? value)
    {
        _ = LoadCurveAsync(value);
    }

    private async Task LoadSamplesAsync(TestSheetSummary? sheet)
    {
        Samples.Clear();
        SelectedSample = null;
        SelectedAnnotation = null;
        CurveLoaded?.Invoke(this, []);

        if (sheet is null)
        {
            return;
        }

        _sampleRows = await _browser.ReadSamplesAsync(sheet.SheetId);
        _sampleSheetId = sheet.SheetId;
        RebuildSamples();
    }

    /// <summary>
    /// 按 <see cref="_sampleRows"/> 重算试件列表：**第一列当标题、其余列做字段表**，列名都用
    /// <see cref="L(string)"/> 取当前语言（浏览器给的是资源键，见 <see cref="SampleRow"/> 的说明）。
    /// 换语言时重跑一遍，选中的那一件按试件号找回来。
    /// </summary>
    private void RebuildSamples()
    {
        var previous = SelectedSample?.SampleId;

        Samples.Clear();
        foreach (var row in _sampleRows)
        {
            var fields = row.ToArray();
            var sampleId = fields.Length > 0 ? fields[0].Value : string.Empty;
            var title = fields.Length > 0
                ? $"{L(fields[0].Key)} {sampleId}"
                : _sampleSheetId;

            Samples.Add(
                new SampleRow(
                    sampleId,
                    title,
                    [.. fields.Skip(1).Select(pair => new KeyValuePair<string, string>(L(pair.Key), pair.Value))]
                )
            );
        }

        SelectedSample =
            Samples.FirstOrDefault(sample => string.Equals(sample.SampleId, previous, StringComparison.Ordinal))
            ?? Samples.FirstOrDefault();
    }

    private async Task LoadCurveAsync(SampleRow? sample)
    {
        if (sample is null || SelectedSheet is null)
        {
            SelectedAnnotation = null;
            CurveLoaded?.Invoke(this, []);
            return;
        }

        var sampleId = sample.SampleId;
        if (string.IsNullOrWhiteSpace(sampleId))
        {
            SelectedAnnotation = null;
            CurveLoaded?.Invoke(this, []);
            return;
        }

        var sheetId = SelectedSheet.SheetId;

        // 老记录开头那几个点是"接触后位移还没归零"时记下的（X 停在旧偏移上，实测 1.2~10.4mm）——
        // 回放时裁掉、让曲线从位移零点开始（用户 2026-10-15）。
        var curve = CurveDisplay.TrimUnzeroedStart(
            await _browser.ReadCurveAsync(sheetId, sampleId)
        );

        // **先**记标注再发事件：页面在事件处理里读 SelectedAnnotation，取的必须是本条曲线的
        SelectedAnnotation = await _browser.ReadAnnotationAsync(sheetId, sampleId);

        CurveLoaded?.Invoke(this, curve);
    }
}
