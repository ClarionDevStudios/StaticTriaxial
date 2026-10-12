namespace ANX.Ui.ViewModels;

using System.Globalization;
using ANX.Control;
using ANX.Ui.Services;

/// <summary>
/// **试样信息填录页**：编辑"这一件试样"的信息（跨径 / 试件宽 / 试件高…）。
/// 与参数页的区别（用户 2026-10-12 明确）：这些值**随试件走** —— 存进明细行、判定时按件取用，
/// 不是全方法共用一份的配置。
/// </summary>
public sealed partial class SampleInfoViewModel : EditableRowsViewModel
{
    private readonly ITestExecution _execution;
    private readonly ICurrentSheetContext? _currentSheet;
    private bool _initializing;

    public SampleInfoViewModel(
        ILocalizationService localization,
        ITestExecution execution,
        ICurrentSheetContext? currentSheet
    )
        : base(localization)
    {
        _execution = execution;
        _currentSheet = currentSheet;
        Title = L("Sample.Title");
        Description = L("Sample.Description");

        InitializeRows(execution.SampleFields);
    }

    /// <summary>试件号（单内顺序号）：默认取"下一个可用的号"，也可手改去编辑已存的某件。</summary>
    [ObservableProperty]
    private string _sampleId = string.Empty;

    /// <summary>所属试验单（没选单时给提示、不让保存）。</summary>
    public string SheetId => _currentSheet?.SheetId ?? string.Empty;

    public bool HasSheet => !string.IsNullOrWhiteSpace(SheetId);

    /// <summary>没选试验单就不让保存（状态栏同时说明原因）。</summary>
    protected override bool CanSaveExtra => HasSheet;

    public override void OnNavigatedTo() => _ = InitializeAsync();

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (!HasSheet)
        {
            // 没选单：字段仍按默认值铺开（可看、可校验），只是不让保存
            ApplyValues(await LoadCoreAsync());
            Status = L("Sample.NoSheet");
            return;
        }

        _initializing = true;
        try
        {
            if (string.IsNullOrWhiteSpace(SampleId))
            {
                SampleId = await _execution.NextSampleIdAsync(SheetId);
            }
        }
        finally
        {
            _initializing = false;
        }

        ApplyValues(await LoadCoreAsync());
        Status = string.Format(CultureInfo.CurrentCulture, L("Sample.Loaded"), SheetId, SampleId);
    }

    partial void OnSampleIdChanged(string value)
    {
        // 换试件号 = 换一件试样：把那一件的信息载回来（初始化时跳过）
        if (!_initializing)
        {
            _ = ReloadForSampleAsync();
        }
    }

    private async Task ReloadForSampleAsync()
    {
        if (!HasSheet || string.IsNullOrWhiteSpace(SampleId))
        {
            return;
        }

        ApplyValues(await LoadCoreAsync());
    }

    protected override async Task<IReadOnlyDictionary<string, string>> LoadCoreAsync()
    {
        if (!HasSheet || string.IsNullOrWhiteSpace(SampleId))
        {
            return Rows.ToDictionary(row => row.Key, row => row.DefaultValue, StringComparer.Ordinal);
        }

        return await _execution.LoadSampleInfoAsync(SheetId, SampleId);
    }

    protected override async Task SaveCoreAsync(IReadOnlyDictionary<string, string> values)
    {
        if (!HasSheet)
        {
            throw new InvalidOperationException(L("Sample.NoSheet"));
        }

        if (string.IsNullOrWhiteSpace(SampleId))
        {
            throw new InvalidOperationException(L("Sample.NoSampleId"));
        }

        await _execution.SaveSampleInfoAsync(SheetId, SampleId, values);
    }
}
