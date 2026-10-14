namespace ANX.Ui.ViewModels;

using System.Globalization;
using ANX.Control;
using ANX.Ui.Services;

/// <summary>
/// **试样信息填录页**：编辑"这一件试样"的信息（跨径 / 试件宽 / 试件高…）。
/// 与参数页的区别（用户 2026-10-12 明确）：这些值**随试件走** —— 存进明细行、判定时按件取用，
/// 不是全方法共用一份的配置。
///
/// 用户 2026-10-13 起本页也以**弹出窗体**打开（见 <see cref="IModalPageService"/>），
/// 所以底部同样给了一个「关闭」。
/// </summary>
public sealed partial class SampleInfoViewModel : EditableRowsViewModel, IModalDialogViewModel
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

    /// <inheritdoc />
    public event EventHandler<bool>? CloseRequested;

    /// <summary>打开时把"这一件"读出来（导航页与弹窗都走这里，用户 2026-10-13）。</summary>
    public override void OnOpened() => _ = InitializeAsync();

    /// <summary>关闭弹窗（改动只有点了「保存」才落地，所以这里不给"确定"语义）。</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, false);

    /// <summary>
    /// 进弹窗前定死"编辑哪一件"：给了试件号就载回那一件的信息，给 null/空 = 新建
    /// （打开时自己取下一个试件号）。调用方是试验执行页 —— 它知道用户在列表里选了哪行。
    /// </summary>
    public void PrepareFor(string? sampleId) => SampleId = sampleId ?? string.Empty;

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (!HasSheet)
        {
            // 没选单：字段仍按默认值铺开（可看、可校验），只是不让保存
            ApplyValues(await LoadCoreAsync());
            SetStatus(L("Sample.NoSheet"), isError: true);
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
        SetStatus(string.Format(CultureInfo.CurrentCulture, L("Sample.Loaded"), SheetId, SampleId));
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
