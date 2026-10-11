namespace ANX.Ui.ViewModels;

using ANX.Control;
using ANX.Core;
using ANX.Core.TestSheets;
using ANX.Ui.Services;

/// <summary>试件行：标题 + 字段表（都是"列名 → 文本"，界面不认识具体含义）。</summary>
public sealed record SampleRow(string Title, IReadOnlyList<KeyValuePair<string, string>> Fields);

/// <summary>
/// 历史数据页 ViewModel：试验单列表（主）→ 试件列表（从）→ 曲线预览 → 导出报表。
/// 报表导出入口就在这里（与"执行页的打印"共用同一套实现）。
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
    private readonly ITestSheetBrowser _browser;
    private readonly IReportExportService _exporter;

    public HistoryViewModel(
        ILocalizationService localization,
        ITestSheetBrowser browser,
        IReportExportService exporter
    )
        : base(localization)
    {
        _browser = browser;
        _exporter = exporter;
        Status = L("History.Ready");
    }

    /// <summary>试验单列表。</summary>
    public ObservableCollection<TestSheetSummary> Sheets { get; } = [];

    /// <summary>选中试验单的试件列表。</summary>
    public ObservableCollection<SampleRow> Samples { get; } = [];

    [ObservableProperty]
    private string _keyword = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private TestSheetSummary? _selectedSheet;

    [ObservableProperty]
    private SampleRow? _selectedSample;

    /// <summary>曲线加载完成（页面据此重绘）。</summary>
    public event EventHandler<IReadOnlyList<CurvePoint>>? CurveLoaded;

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

        var sheets = await _browser.QueryAsync(
            string.IsNullOrWhiteSpace(Keyword) ? null : Keyword
        );
        foreach (var sheet in sheets)
        {
            Sheets.Add(sheet);
        }

        Status = $"{L("History.Found")} {Sheets.Count} {L("History.SheetsUnit")}";
        SelectedSheet = Sheets.FirstOrDefault();
    }

    /// <summary>导出选中试验单的中文报表。</summary>
    [RelayCommand]
    private Task ExportAsync() => ExportCoreAsync("zh-CN");

    /// <summary>导出选中试验单的英文报表。</summary>
    [RelayCommand]
    private Task ExportEnglishAsync() => ExportCoreAsync("en-US");

    partial void OnSelectedSheetChanged(TestSheetSummary? value)
    {
        _ = LoadSamplesAsync(value);
    }

    partial void OnSelectedSampleChanged(SampleRow? value)
    {
        _ = LoadCurveAsync(value);
    }

    private async Task ExportCoreAsync(string culture)
    {
        if (SelectedSheet is null)
        {
            Status = L("History.SelectSheetFirst");
            return;
        }

        try
        {
            var path = await _exporter.ExportAsync(SelectedSheet.SheetId, culture);
            Status = $"{L("History.Exported")} {path}";
        }
        catch (Exception ex)
        {
            Status = $"{L("History.ExportFailed")}: {ex.Message}";
        }
    }

    private async Task LoadSamplesAsync(TestSheetSummary? sheet)
    {
        Samples.Clear();
        SelectedSample = null;
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
            Samples.Add(new SampleRow(title, [.. fields.Skip(1)]));
        }

        SelectedSample = Samples.FirstOrDefault();
    }

    private async Task LoadCurveAsync(SampleRow? sample)
    {
        if (sample is null || SelectedSheet is null)
        {
            CurveLoaded?.Invoke(this, []);
            return;
        }

        var sampleId = sample.Title.Split(' ', 2)[0];
        var curve = await _browser.ReadCurveAsync(SelectedSheet.SheetId, sampleId);
        CurveLoaded?.Invoke(this, curve);
    }
}
