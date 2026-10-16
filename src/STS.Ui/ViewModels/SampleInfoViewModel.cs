namespace STS.Ui.ViewModels;

using System.Globalization;
using STS.Control;
using STS.Control.Methods;
using STS.Ui.Services;

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
    private bool _backfillMode;

    public SampleInfoViewModel(
        ILocalizationService localization,
        ITestExecution execution,
        ICurrentSheetContext? currentSheet,
        ICurrentMethodContext? methods = null
    )
        : base(localization)
    {
        _execution = execution;
        _currentSheet = currentSheet;

        InitializeRows(execution.SampleFields);
        RefreshTexts();

        // 换试验方法：试样信息字段整套换掉（键名不同），行与"上次存了哪件"一起重来
        // （页面在方法切换后重开弹窗时看到的就是新方法的字段）
        if (methods is not null)
        {
            methods.Changed += (_, _) =>
            {
                ResetRows(_execution.SampleFields);
                SampleId = string.Empty;
                LastSavedSampleId = string.Empty;
            };
        }

        // 换语言：页标题与描述跟着变（参数行由基类通知）—— ViewModel 是单例，构造时算的那份会过期
        localization.LanguageChanged += (_, _) => RefreshTexts();
    }

    /// <summary>按当前语言重建页标题与描述。</summary>
    public void RefreshTexts()
    {
        Title = L(_backfillMode ? "Sample.BackfillTitle" : "Sample.Title");
        Description = L(_backfillMode ? "Sample.BackfillDescription" : "Sample.Description");
    }

    /// <summary>
    /// **试验后数据补录模式**（用户 2026-10-16 定的 Q3 口径）：从执行页「补录」进 ——
    /// 只有标记了 <see cref="ParameterDefinition.PostTest"/> 的字段可写（质量 / 含水率…），
    /// 其余试样参数只读；试件号也钉死在被补录的那一件上。
    /// </summary>
    public bool IsBackfillMode => _backfillMode;

    /// <summary>试件号输入框可用性（补录模式钉死，不允许换件）。</summary>
    public bool IsSampleIdEditable => !_backfillMode;

    /// <summary>「恢复默认」可用性（补录模式只动试验后字段，恢复默认会误导 —— 直接置灰）。</summary>
    public bool CanRestoreDefaults => !_backfillMode;

    /// <summary>试件号（单内顺序号）：默认取"下一个可用的号"，也可手改去编辑已存的某件。</summary>
    [ObservableProperty]
    private string _sampleId = string.Empty;

    /// <summary>
    /// **最近一次存成功**的试件号（没存过就是空）。执行页在弹窗关掉后用它把列表**选中到刚存的那一件**
    /// （用户 2026-10-16："新建试样信息后，列表中应选中刚新建的那条试样信息"）。
    ///
    /// 为什么要单独记一个：<see cref="SampleId"/> 在弹窗里可能被改过又没保存，直接读它会选错人。
    /// </summary>
    public string LastSavedSampleId { get; private set; } = string.Empty;

    /// <summary>所属试验单（没选单时给提示、不让保存）。</summary>
    public string SheetId => _currentSheet?.SheetId ?? string.Empty;

    public bool HasSheet => !string.IsNullOrWhiteSpace(SheetId);

    /// <summary>没选试验单就不让保存（状态栏同时说明原因）。</summary>
    protected override bool CanSaveExtra => HasSheet;

    /// <inheritdoc />
    public event EventHandler<bool>? CloseRequested;

    /// <summary>
    /// 打开时把"这一件"读出来（导航页与弹窗都走这里，用户 2026-10-13）；
    /// 顺带按当前语言重刷页标题与描述。
    /// </summary>
    public override void OnOpened()
    {
        RefreshTexts();
        _ = InitializeAsync();
    }

    /// <summary>关闭弹窗（改动只有点了「保存」才落地，所以这里不给"确定"语义）。</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, false);

    /// <summary>
    /// 进弹窗前定死"编辑哪一件"：给了试件号就载回那一件的信息，给 null/空 = 新建
    /// （打开时自己取下一个试件号）。调用方是试验执行页 —— 它知道用户在列表里选了哪行。
    /// </summary>
    public void PrepareFor(string? sampleId, bool backfill = false)
    {
        _backfillMode = backfill;
        OnPropertyChanged(nameof(IsBackfillMode));
        OnPropertyChanged(nameof(IsSampleIdEditable));
        OnPropertyChanged(nameof(CanRestoreDefaults));

        SampleId = sampleId ?? string.Empty;

        // 新开一次弹窗就清掉"上次存了哪一件"：这一次没保存的话，执行页不该去选中上一次那件
        LastSavedSampleId = string.Empty;

        ApplyBackfillLocks();
        RefreshTexts();
    }

    /// <summary>补录模式：非"试验后字段"的行全部锁住（界面按行的 IsEditable 置灰）。</summary>
    private void ApplyBackfillLocks()
    {
        foreach (var row in Rows)
        {
            row.IsLocked = _backfillMode && !row.Definition.PostTest;
        }
    }

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

        // 补录模式只提交"试验后字段"——界面已只读，这里再收一道口：
        // 就算别处把值改了，也不会覆盖库里原有的试样参数（用户 2026-10-16 Q3）。
        var payload = _backfillMode
            ? values
                .Where(pair =>
                    Rows.FirstOrDefault(row =>
                        string.Equals(row.Key, pair.Key, StringComparison.Ordinal)
                    )?.Definition.PostTest == true
                )
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : values;

        await _execution.SaveSampleInfoAsync(SheetId, SampleId, payload);
    }

    /// <summary>保存成功后记下"刚存的是哪一件"（执行页据此选中列表里的那一行）。</summary>
    protected override void AfterSave(IReadOnlyDictionary<string, string> values)
    {
        base.AfterSave(values);
        LastSavedSampleId = SampleId;
    }
}
