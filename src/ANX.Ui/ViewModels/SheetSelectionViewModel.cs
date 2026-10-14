namespace ANX.Ui.ViewModels;

using System.Globalization;
using ANX.Control;
using ANX.Core.TestSheets;
using ANX.Ui.Services;

/// <summary>试验单编辑窗体的「试验方式」三选一（用户 2026-10-14 定的口径，与现役一致）。</summary>
public enum SheetEditMode
{
    /// <summary>新建试验项目：单号自动生成、按"试验目的"预填温度与速率。</summary>
    New = 0,

    /// <summary>上次试验项目：载入**上一次新建**的那张单的信息（试验信息表为空时不可选）。</summary>
    Last = 1,

    /// <summary>其他试验项目：**直接采用**所选那张单（后续新增的试样归属它）。</summary>
    Existing = 2,
}

/// <summary>试验单信息的一行（界面按方法声明的字段生成，不认识具体字段含义）。</summary>
public partial class SheetFieldRow(ParameterDefinition definition) : ObservableObject
{
    public string Key { get; } = definition.Key;

    public string Label { get; } = definition.DisplayName;

    public string Unit { get; } = definition.Unit;

    public ParameterKind Kind { get; } = definition.Kind;

    public ParameterGroup Group { get; } = definition.Group;

    /// <summary>方法给的默认值（新建时先用它；参数表里有同键的值再覆盖）。</summary>
    public string DefaultValue { get; } = definition.DefaultValue;

    /// <summary>下拉选项（<see cref="ParameterKind.Choice"/> 才有）。</summary>
    public IReadOnlyList<ParameterOption> Options { get; } = definition.Options ?? [];

    /// <summary>当前值（文本；下拉时是选项的 Value）。</summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>只读（单号、试验时间；或"采用已有单"模式下展示的信息）。</summary>
    [ObservableProperty]
    private bool _isReadOnly;

    public bool IsChoice => Kind == ParameterKind.Choice;

    public bool IsDate => Kind == ParameterKind.Date;

    public bool IsPlain => Kind is ParameterKind.Text or ParameterKind.Number;

    /// <summary>
    /// 下拉与日期控件没有"只读"这一说，只能整只禁用 —— 照现役 `SetControlsReadonly`：
    /// 文本框用 ReadOnly，下拉框 / 日期框用 Enabled := False。
    /// </summary>
    public bool IsEnabledForEdit => !IsReadOnly;

    partial void OnIsReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEnabledForEdit));
    }
}

/// <summary>
/// 试验单编辑窗体（弹出窗体）：顶部「试验方式」三选一 + 单号（只读、自动生成）+ 字段区（照现役次序，
/// 两列按行配对：左列工程与试验条件、右列体积指标 + 接样日期 / 试验时间 / 试验员）
/// + 模板按钮 + "其他试验项目"时的已有单列表 + 确定 / 取消。
///
/// 口径（用户 2026-10-14）：
/// <list type="bullet">
///   <item>单号**自动生成、只读**（界面显示的是预览值，真正发号在确定时）；</item>
///   <item>「上次试验项目」= 上一次新建的那张单（不新建）；试验信息表为空时**不可选**；整页只读；</item>
///   <item>「其他试验项目」= 直接采用所选单（后续试样归它）；**可编辑并写回该单**
///         —— 这是"改已有单信息"的入口（用户 2026-10-14 更正）；</item>
///   <item>保存（新建 / 改单）前都做输入值验证；「上次试验项目」整页只读、不动库；</item>
///   <item>「读取模板」**只在"新建试验项目"模式下可用**；「存为模板」把当前表单内容存成模板；</item>
///   <item>保存时把单上的加载速率写回参数表（实际控制按单上填的走）；新建时用参数表当前值预填；</item>
///   <item>必填清单由**方法层**判定并抛消息（项目名称 / 试验规范 / 试验目的 / 试件尺寸 / 混合料类型 /
///         试验温度 / 加载速率），体积指标 6 项允许留空 —— 界面只显示消息。</item>
/// </list>
/// </summary>
public partial class SheetSelectionViewModel : ViewModelBase, IModalDialogViewModel
{
    private readonly ITestSheetBrowser _browser;
    private readonly ITestSheetEditor _editor;
    private readonly ITestParameters _parameters;
    private readonly ICurrentSheetContext _current;

    /// <summary>单号与前缀：本次沿用工程里的固定前缀（以后接到设置里再改）。</summary>
    private const string NumberPrefix = "WT";

    public SheetSelectionViewModel(
        ILocalizationService localization,
        ITestSheetBrowser browser,
        ITestSheetEditor editor,
        ITestParameters parameters,
        ICurrentSheetContext current
    )
        : base(localization)
    {
        _browser = browser;
        _editor = editor;
        _parameters = parameters;
        _current = current;

        Rows = [.. editor.SheetInfoFields.Select(definition => new SheetFieldRow(definition))];

        foreach (var row in Rows)
        {
            row.PropertyChanged += OnFieldChanged;
        }

        Status = L("Sheet.Ready");
    }

    /// <inheritdoc />
    public event EventHandler<bool>? CloseRequested;

    /// <summary>
    /// 全部字段行，**顺序 = 现役版面**：现役窗体是两列网格，左列工程与试验条件、右列体积指标 +
    /// 接样日期 / 试验时间 / 试验员，两列按行配对；界面用两列 UniformGrid 按行填充，直接照这个顺序摆即可。
    /// </summary>
    public ObservableCollection<SheetFieldRow> Rows { get; }

    /// <summary>"其他试验项目"时用于挑选的已有单列表。</summary>
    public ObservableCollection<TestSheetSummary> Sheets { get; } = [];

    [ObservableProperty]
    private SheetEditMode _mode = SheetEditMode.New;

    [ObservableProperty]
    private TestSheetSummary? _selectedSheet;

    /// <summary>单号那一行后面的提示（新建 = 自动生成只读；采用已有单 = 沿用该单）。</summary>
    [ObservableProperty]
    private string _sheetIdHint = string.Empty;

    /// <summary>哪些项允许留空 —— 由方法给的分组算出来（体积指标组），界面不写死字段名。</summary>
    [ObservableProperty]
    private string _optionalHint = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>模板表里有没有内容（决定「读取模板」能不能点）。</summary>
    [ObservableProperty]
    private bool _hasTemplate;

    /// <summary>正在载入表单（载入期间不要触发字段联动）。</summary>
    private bool _loading;

    // ── 三个单选项（互斥） ───────────────────────────────────────────────

    public bool IsNewMode
    {
        get => Mode == SheetEditMode.New;
        set
        {
            if (value)
            {
                Mode = SheetEditMode.New;
            }
        }
    }

    public bool IsLastMode
    {
        get => Mode == SheetEditMode.Last;
        set
        {
            if (value)
            {
                Mode = SheetEditMode.Last;
            }
        }
    }

    public bool IsExistingMode
    {
        get => Mode == SheetEditMode.Existing;
        set
        {
            if (value)
            {
                Mode = SheetEditMode.Existing;
            }
        }
    }

    /// <summary>「上次试验项目」可不可选：试验信息表里有内容才可（用户 2026-10-14）。</summary>
    public bool CanUseLastMode => Sheets.Count > 0;

    /// <summary>「读取模板」只在"新建试验项目"模式下可用（用户 2026-10-14）。</summary>
    public bool CanReadTemplate => Mode == SheetEditMode.New && HasTemplate;

    /// <summary>「存为模板」：当前表单有内容可存。</summary>
    public bool CanSaveTemplate => Mode != SheetEditMode.Existing || SelectedSheet is not null;

    /// <summary>「确定」：新建随时可点（方法层校验必填）；"上次"要有单；"其他"要有选中项。</summary>
    public bool CanConfirm =>
        Mode switch
        {
            SheetEditMode.Last => Sheets.Count > 0,
            SheetEditMode.Existing => SelectedSheet is not null,
            _ => true,
        };

    partial void OnModeChanged(SheetEditMode value)
    {
        // 换试验方式 = 换一次填表：把上一条提示（"项目名称不能为空"之类）清掉，免得看得心慌
        Status = L("Sheet.Ready");

        OnPropertyChanged(nameof(IsNewMode));
        OnPropertyChanged(nameof(IsLastMode));
        OnPropertyChanged(nameof(IsExistingMode));
        OnPropertyChanged(nameof(CanReadTemplate));
        OnPropertyChanged(nameof(CanSaveTemplate));
        OnPropertyChanged(nameof(CanConfirm));
        ReadTemplateCommand.NotifyCanExecuteChanged();
        SaveTemplateCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();

        _ = LoadForModeAsync();
    }

    partial void OnSelectedSheetChanged(TestSheetSummary? value)
    {
        OnPropertyChanged(nameof(CanSaveTemplate));
        OnPropertyChanged(nameof(CanConfirm));
        SaveTemplateCommand.NotifyCanExecuteChanged();
        ConfirmCommand.NotifyCanExecuteChanged();

        if (Mode == SheetEditMode.Existing)
        {
            _ = ApplySheetAsync(value?.SheetId, editable: true);
        }
    }

    partial void OnHasTemplateChanged(bool value)
    {
        OnPropertyChanged(nameof(CanReadTemplate));
        ReadTemplateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>打开窗体：先取单列表（"上次"要用、也决定它可不可选），再按当前模式载入。</summary>
    public override void OnOpened() => _ = InitializeAsync();

    private async Task InitializeAsync()
    {
        // 文案在这里现取：ViewModel 是单例，语言切换后（弹窗重开时）要跟着变
        OptionalHint =
            L("Sheet.OptionalHint")
            + string.Join(
                "、",
                Rows.Where(row => row.Group == ParameterGroup.SheetVolume)
                    .Select(row => row.Label)
            );
        SheetIdHint = L("Sheet.SheetIdAuto");

        await ReloadSheetsAsync().ConfigureAwait(true);
        HasTemplate = await _editor.HasTemplateAsync().ConfigureAwait(true);
        await LoadForModeAsync().ConfigureAwait(true);
    }

    private async Task ReloadSheetsAsync()
    {
        Sheets.Clear();

        foreach (var sheet in await _browser.QueryAsync(null).ConfigureAwait(true))
        {
            Sheets.Add(sheet);
        }

        // 查询按创建时间倒序 → 第一条就是"上一次新建的那张单"
        SelectedSheet = Sheets.FirstOrDefault();
        OnPropertyChanged(nameof(CanUseLastMode));
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    /// <summary>按当前试验方式把表单填起来。</summary>
    private async Task LoadForModeAsync()
    {
        switch (Mode)
        {
            case SheetEditMode.New:
                await ApplyNewDefaultsAsync().ConfigureAwait(true);
                break;

            case SheetEditMode.Last:
                await ApplySheetAsync(Sheets.FirstOrDefault()?.SheetId, editable: false)
                    .ConfigureAwait(true);
                break;

            default:
                await ApplySheetAsync(SelectedSheet?.SheetId, editable: true).ConfigureAwait(true);
                break;
        }
    }

    /// <summary>
    /// 新建模式的初值：各字段默认值 + **参数表当前的加载速率** + 试验目的对应的温度/速率 + 当前日期时间；
    /// 单号显示"下一个号"的预览值（只读）。
    /// </summary>
    private async Task ApplyNewDefaultsAsync()
    {
        _loading = true;
        try
        {
            var stored = await _parameters.LoadAsync().ConfigureAwait(true);

            foreach (var row in Rows)
            {
                row.IsReadOnly = false;

                if (AutoKeys.Contains(row.Key))
                {
                    continue; // 单号与试验时间由 ApplyAutoValuesAsync 给（模板里也没有这两项）
                }

                if (row.IsDate)
                {
                    row.Value = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else if (stored.TryGetValue(row.Key, out var fromParameters))
                {
                    // 用户 2026-10-14：单上的试验条件**新建时用参数表当前值预填**
                    //（现役 T0715 的加载速率在参数表里；温度参数表里没有，就落回声明里的缺省值）
                    row.Value = fromParameters;
                }
                else
                {
                    // 其余取方法给的缺省值（下拉落在第 1 项、温度 15…）
                    row.Value = row.DefaultValue;
                }
            }

            await ApplyAutoValuesAsync().ConfigureAwait(true);

            // 注意：这里**不**调 ApplyDependentDefaults —— 那是"用户改了试验目的"时的联动
            //（现役在此硬写 15℃/50），初始化时调会把参数表预填好的加载速率冲成 50。
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// **自动值**（新建模式专属）：单号显示"下一个号"的**预览值**、试验时间取当前时刻，两项都只读。
    /// 单独抽出来是因为"新建初值"与"读取模板"两条路都要回到这个状态 —— 模板表里没有这两项，
    /// 读模板时不能用空串把它们冲掉（用户 2026-10-14）。
    /// </summary>
    private async Task ApplyAutoValuesAsync()
    {
        var peeked = await _editor.PeekNextSheetIdAsync().ConfigureAwait(true);

        SetValue("SheetID", peeked);
        SetValue("CreatedOn", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        SetReadOnly("SheetID");
        SetReadOnly("CreatedOn");

        SheetIdHint = L("Sheet.SheetIdAuto");
    }

    /// <summary>方法自动填的字段（不是模板内容）：单号、试验时间。</summary>
    private static readonly string[] AutoKeys = ["SheetID", "CreatedOn"];

    /// <summary>
    /// 采用某张已有单（"上次试验项目" / "其他试验项目"）：把它的信息填进表单。
    /// **上次** = 整页只读（照现役 `SetControlsReadonly(ItemIndex = 1)`：只是"拿这张单接着做"）；
    /// **其他** = 可编辑（用户 2026-10-14 更正：这是"改已有单信息"的入口，确定时写回该单）。
    /// 两种方式下**单号与试验时间都只读**（单号是主键、试验时间是该单的原始记录）。
    /// </summary>
    private async Task ApplySheetAsync(string? sheetId, bool editable)
    {
        if (string.IsNullOrWhiteSpace(sheetId))
        {
            return;
        }

        _loading = true;
        try
        {
            var values = await _editor.ReadSheetInfoAsync(sheetId).ConfigureAwait(true);

            foreach (var row in Rows)
            {
                row.Value = values.GetValueOrDefault(row.Key) ?? string.Empty;
                row.IsReadOnly = !editable;
            }

            // 可编辑时把"改这里会写回该单"讲明白，免得当成新建
            SheetIdHint = L(editable ? "Sheet.SheetIdEdit" : "Sheet.SheetIdPicked");

            if (editable)
            {
                SetReadOnly("SheetID");
                SetReadOnly("CreatedOn");
            }
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>字段被改动（非载入期间）：问方法层要"跟着变的其它字段"（现役：试验目的 → 温度/速率）。</summary>
    private void OnFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading
            || sender is not SheetFieldRow row
            || e.PropertyName != nameof(SheetFieldRow.Value))
        {
            return;
        }

        ApplyDependentDefaults(row.Key, row.Value);
    }

    private void ApplyDependentDefaults(string changedKey, string value)
    {
        var dependent = _editor.ResolveDependentDefaults(changedKey, value);
        if (dependent.Count == 0)
        {
            return;
        }

        _loading = true;
        try
        {
            foreach (var (key, text) in dependent)
            {
                SetValue(key, text);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void SetValue(string key, string value)
    {
        if (Rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal)) is { } row)
        {
            row.Value = value;
        }
    }

    private void SetReadOnly(string key)
    {
        if (Rows.FirstOrDefault(row => string.Equals(row.Key, key, StringComparison.Ordinal)) is { } row)
        {
            row.IsReadOnly = true;
        }
    }

    /// <summary>
    /// 「读取模板」（仅新建模式）：把模板内容填进表单（缺省值），之后照样可以改。
    /// **单号与试验时间不归模板管** —— 读完仍是"预览号 + 当前时刻"（用户 2026-10-14：
    /// 读模板后试验时间不要清空，按当前时间填写）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReadTemplate))]
    private async Task ReadTemplateAsync()
    {
        var values = await _editor.ReadTemplateAsync().ConfigureAwait(true);

        _loading = true;
        try
        {
            foreach (var row in Rows)
            {
                if (AutoKeys.Contains(row.Key))
                {
                    continue; // 模板里没有这两项，别用空串冲掉
                }

                row.Value = values.GetValueOrDefault(row.Key) ?? string.Empty;
            }
        }
        finally
        {
            _loading = false;
        }

        // 顺手把"当前时间"重新盖一次：模板内容放一会儿再读也不该看到陈旧的时间
        await ApplyAutoValuesAsync().ConfigureAwait(true);

        Status = L("Sheet.TemplateLoaded");
    }

    /// <summary>「存为模板」：把当前表单内容存成模板。</summary>
    [RelayCommand(CanExecute = nameof(CanSaveTemplate))]
    private async Task SaveTemplateAsync()
    {
        await _editor.SaveTemplateAsync(Values()).ConfigureAwait(true);
        HasTemplate = true;
        Status = L("Sheet.TemplateSaved");
    }

    /// <summary>
    /// 确定：新建一张单 / 把改过的内容写回选中的单 / 采用上次那张单，然后把该单的加载速率写回参数表。
    /// 新建与改单都要过方法层的输入值验证（不通过就抛消息，弹窗不关）；「上次试验项目」不动库。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        try
        {
            string? sheetId = null;
            var updated = false;

            switch (Mode)
            {
                case SheetEditMode.New:
                    sheetId = await _editor.CreateSheetAsync(Values()).ConfigureAwait(true);
                    break;

                case SheetEditMode.Existing:
                    // 改已有单信息（用户 2026-10-14 更正）：写回该单，单号不变
                    sheetId = SelectedSheet?.SheetId;

                    if (!string.IsNullOrWhiteSpace(sheetId))
                    {
                        await _editor.UpdateSheetAsync(sheetId, Values()).ConfigureAwait(true);
                        updated = true;
                    }

                    break;

                default:
                    sheetId = Sheets.FirstOrDefault()?.SheetId;
                    break;
            }

            if (string.IsNullOrWhiteSpace(sheetId))
            {
                Status = L("Sheet.SelectFirst");
                return;
            }

            _current.Set(sheetId, Values().GetValueOrDefault("ProjectName") ?? string.Empty);

            // 实际控制按单上填的加载速率走（用户 2026-10-14）
            await _editor.PushTestConditionsAsync(sheetId).ConfigureAwait(true);

            Status = $"{(updated ? L("Sheet.Updated") : L("Sheet.Selected"))} {sheetId}";
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            Status = $"{L("Sheet.CreateFailed")}: {ex.Message}";
        }
    }

    /// <summary>取消：什么都不做，关掉弹窗。</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private Dictionary<string, string> Values() =>
        Rows.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
}
