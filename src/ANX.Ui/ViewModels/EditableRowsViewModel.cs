namespace ANX.Ui.ViewModels;

using System.Globalization;
using ANX.Control;
using ANX.Ui.Services;

/// <summary>
/// 一行可编辑数据：键 / 显示名 / 编辑类型 / 范围提示 / 当前值 / 校验状态 / 是否改过。
/// 界面不认识任何具体含义 —— 全部来自字段声明（方法参数与试样信息共用同一套 <see cref="ParameterDefinition"/>）。
/// </summary>
public partial class ParameterRow(ParameterDefinition definition, string hint) : ObservableObject
{
    public ParameterDefinition Definition { get; } = definition;

    public string Key => definition.Key;

    public string DisplayName => definition.DisplayName;

    public ParameterKind Kind => definition.Kind;

    public ParameterScope Scope => definition.Scope;

    public bool IsSwitch => definition.Kind == ParameterKind.Switch;

    /// <summary>下拉列表（通道号这类固定选项；现役用单选组，我们改下拉省空间）。</summary>
    public bool IsChoice => definition.Kind == ParameterKind.Choice;

    /// <summary>下拉列表的选项。</summary>
    public IReadOnlyList<ParameterOption> Options => definition.Options ?? [];

    /// <summary>数字与文本都走文本框（与开关 / 下拉互斥显示）。</summary>
    public bool IsTextBox =>
        definition.Kind is ParameterKind.Number or ParameterKind.Text;

    /// <summary>一句话说明（照现役窗体每项的注释），显示在名称下方。</summary>
    public string Description => definition.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(definition.Description);

    /// <summary>现役有、本软件还没功能：置灰 + 标注"待接线"。</summary>
    public bool PendingIntegration => definition.PendingIntegration;

    /// <summary>可编辑（待接线的项不能改）。</summary>
    public bool IsEditable => !definition.PendingIntegration;

    /// <summary>单位与范围提示（"mm/min  0.1~50"），显示在编辑框右侧。</summary>
    public string Hint { get; } = hint;

    /// <summary>默认值（"恢复默认"用）。</summary>
    public string DefaultValue => definition.DefaultValue;

    [ObservableProperty]
    private string _value = definition.DefaultValue;

    /// <summary>校验错误文案；空串表示合法。</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    /// <summary>与"上次载入 / 保存的值"不同。</summary>
    [ObservableProperty]
    private bool _isDirty;

    public bool HasError => ErrorText.Length > 0;

    /// <summary>开关绑定点：值仍以 "1"/"0" 文本存储，与库里的存法一致。</summary>
    public bool SwitchValue
    {
        get => Value == "1";
        set => Value = value ? "1" : "0";
    }

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(SwitchValue));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
}

/// <summary>
/// 可编辑行集合的基类：建行、校验、脏状态、保存 / 重新载入 / 恢复默认与状态提示。
/// **参数配置页**与**试样信息填录页**共用它 —— 前者按作用时点分区、读写方法参数表；
/// 后者编辑"这一件试样"的信息、读写明细行。
/// </summary>
public abstract partial class EditableRowsViewModel : ViewModelBase
{
    /// <summary>上次载入 / 保存的值的快照（脏状态与"重新载入"用它比对）。</summary>
    private Dictionary<string, string> _saved = new(StringComparer.Ordinal);

    protected EditableRowsViewModel(ILocalizationService localization) : base(localization) { }

    /// <summary>扁平清单（校验 / 保存 / 脏状态用；界面按需再分组渲染）。</summary>
    public ObservableCollection<ParameterRow> Rows { get; } = [];

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>有未保存的修改。</summary>
    public bool HasChanges => Rows.Any(row => row.IsDirty);

    /// <summary>有校验错误（有错就挡住保存）。</summary>
    public bool HasErrors => Rows.Any(row => row.HasError);

    /// <summary>保存闸门：不忙、且没有校验错误。</summary>
    public bool CanSave => !IsBusy && !HasErrors && CanSaveExtra;

    /// <summary>派生类可以再加闸门条件（例如"试样信息页没选试验单时不让保存"）。</summary>
    protected virtual bool CanSaveExtra => true;

    /// <summary>确认框文案里的对象名（一般是页面标题）。</summary>
    protected string ScopeName => Title;

    /// <summary>按声明建行（派生类构造时调用一次）并订阅变化。</summary>
    protected void InitializeRows(IEnumerable<ParameterDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            Rows.Add(new ParameterRow(definition, BuildHint(definition)));
        }

        foreach (var row in Rows)
        {
            row.PropertyChanged += (_, _) => OnRowChanged();
        }
    }

    /// <summary>把一组值写进行里并记快照（载入完成后调用）。</summary>
    protected void ApplyValues(IReadOnlyDictionary<string, string> values)
    {
        _saved = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in Rows)
        {
            var stored = values.TryGetValue(row.Key, out var value) ? value : row.DefaultValue;
            row.Value = stored;
            _saved[row.Key] = stored;
        }

        RefreshValidation();
        RefreshDirty();
        RaiseStateChanged();
    }

    /// <summary>派生类保存：把当前值写回它自己的存储（参数表 / 明细行）。</summary>
    protected abstract Task SaveCoreAsync(IReadOnlyDictionary<string, string> values);

    /// <summary>派生类载入：读回当前值。</summary>
    protected abstract Task<IReadOnlyDictionary<string, string>> LoadCoreAsync();

    /// <summary>从存储重新载入（有未保存修改时先确认）。</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (HasChanges && !await ConfirmAsync("Param.ReloadConfirm", ScopeName))
        {
            return;
        }

        ApplyValues(await LoadCoreAsync());
        SetStatus(L("Param.Loaded"));
    }

    /// <summary>所有字段恢复为声明的默认值（不落库，等用户保存；先确认）。</summary>
    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        if (!await ConfirmAsync("Param.RestoreDefaultsConfirm", ScopeName))
        {
            return;
        }

        foreach (var row in Rows)
        {
            row.Value = row.DefaultValue;
        }

        RefreshValidation();
        RefreshDirty();
        RaiseStateChanged();
        SetStatus(L("Param.RestoreDefaultsDone"));
    }

    /// <summary>保存（有校验错误时拒绝并提示）。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        RefreshValidation();

        if (HasErrors)
        {
            SetStatus(string.Format(
                CultureInfo.CurrentCulture,
                L("Param.Invalid"),
                Rows.Count(row => row.HasError)
            ), isError: true);
            return;
        }

        try
        {
            IsBusy = true;
            var values = Rows.ToDictionary(
                row => row.Key,
                row => row.Value ?? string.Empty,
                StringComparer.Ordinal
            );

            await SaveCoreAsync(values);
            _saved = values;
            RefreshDirty();
            SetStatus(L("Param.Saved"));
            AfterSave(values);
        }
        catch (Exception ex)
        {
            SetStatus($"{L("Param.SaveFailed")}: {ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
            RaiseStateChanged();
        }
    }

    private void OnRowChanged()
    {
        RefreshValidation();
        RefreshDirty();
        RaiseStateChanged();
    }

    /// <summary>校验每一行并写回行内错误文案（保存闸门也看它）。</summary>
    private void RefreshValidation()
    {
        foreach (var row in Rows)
        {
            var error = ParameterValidator.Validate(row.Definition, row.Value, out _);
            row.ErrorText = error switch
            {
                ParameterValidationError.None => string.Empty,
                ParameterValidationError.Required => L("Param.Error.Required"),
                ParameterValidationError.NotANumber => L("Param.Error.NotANumber"),
                ParameterValidationError.InvalidSwitch => L("Param.Error.InvalidSwitch"),
                ParameterValidationError.OutOfRange => string.Format(
                    CultureInfo.CurrentCulture,
                    L("Param.Error.OutOfRange"),
                    Format(row.Definition.Minimum),
                    Format(row.Definition.Maximum)
                ),
                _ => string.Empty,
            };
        }
    }

    private void RefreshDirty()
    {
        foreach (var row in Rows)
        {
            row.IsDirty =
                !_saved.TryGetValue(row.Key, out var saved)
                || !string.Equals(saved, row.Value, StringComparison.Ordinal);
        }
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>派生类自己的闸门条件变了（例如换了试验单）时调用，刷新保存按钮。</summary>
    protected void RaiseCanSaveChanged() => RaiseStateChanged();

    /// <summary>保存成功后的钩子（派生类用来把新值立刻推给设备 / 界面）。</summary>
    protected virtual void AfterSave(IReadOnlyDictionary<string, string> values) { }

    private string BuildHint(ParameterDefinition definition)
    {
        var range =
            definition.Kind == ParameterKind.Number
            && (definition.Minimum is not null || definition.Maximum is not null)
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    L("Param.RangeHint"),
                    Format(definition.Minimum),
                    Format(definition.Maximum)
                )
                : string.Empty;

        if (string.IsNullOrEmpty(definition.Unit))
        {
            return range;
        }

        return string.IsNullOrEmpty(range) ? definition.Unit : $"{definition.Unit}  {range}";
    }

    private static string Format(double? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";

    /// <summary>确认框（重新载入 / 恢复默认这类会丢弃编辑内容的动作先问一声）。</summary>
    private Task<bool> ConfirmAsync(string messageKey, params object[] args) =>
        AppDialogs.ConfirmAsync(
            L("Param.ConfirmTitle"),
            string.Format(CultureInfo.CurrentCulture, L(messageKey), args),
            L("Common.Yes"),
            L("Common.No")
        );
}
