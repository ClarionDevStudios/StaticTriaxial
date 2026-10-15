namespace ANX.Ui.ViewModels;

using ANX.Control;
using ANX.Core;
using ANX.Core.TestSheets;
using ANX.Ui.Services;

/// <summary>
/// 试件行：**试件号** + 显示标题 + 字段表（都是"列名 → 文本"，界面不认识具体含义）。
///
/// 试件号单独拿出来，是因为曲线是按 <c>单号-试件号</c> 存的（<c>BendingTestDetail.SampleKey</c>）。
/// 之前从显示标题里 <c>Split(' ')[0]</c> 取，取到的是**列名**（"SampleID"）而不是值 ——
/// 于是历史页的曲线一直读不到（回放图总是空的）。2026-10-13 核对回放标注时发现并修掉。
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

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = L("History.Recompute"),
            Content = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                L("History.RecomputeConfirm"),
                sheet.SheetId
            ),
            PrimaryButtonText = L("Common.Yes"),
            SecondaryButtonText = L("Common.No"),
            IsCloseButtonEnabled = false,
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
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
            SetStatus($"{L("History.RecomputeFailed")}: {ex.Message}", isError: true);
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

        var rows = await _browser.ReadSamplesAsync(sheet.SheetId);
        foreach (var row in rows)
        {
            // 第一列当标题，其余列做字段表；界面不认识任何具体字段名
            var fields = row.ToArray();
            var title = fields.Length > 0 ? $"{fields[0].Key} {fields[0].Value}" : sheet.SheetId;
            var sampleId = fields.Length > 0 ? fields[0].Value : string.Empty;
            Samples.Add(new SampleRow(sampleId, title, [.. fields.Skip(1)]));
        }

        SelectedSample = Samples.FirstOrDefault();
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

        // 老记录的首点是"启动试验时那个位置"（bug 期间的库），回放时按定义抹平到位移零点。
        // 只影响画面：库里与报表用的是原始序列。
        var curve = CurveDisplay.ZeroFirstPointX(
            await _browser.ReadCurveAsync(sheetId, sampleId)
        );

        // **先**记标注再发事件：页面在事件处理里读 SelectedAnnotation，取的必须是本条曲线的
        SelectedAnnotation = await _browser.ReadAnnotationAsync(sheetId, sampleId);

        CurveLoaded?.Invoke(this, curve);
    }
}
