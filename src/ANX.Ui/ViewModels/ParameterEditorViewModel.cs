namespace ANX.Ui.ViewModels;

using System.Globalization;
using ANX.Control;
using ANX.Ui.Services;

/// <summary>
/// 一行参数编辑：键 / 显示名 / 单位 / 编辑类型 / 范围提示 / 当前值 / 校验状态 / 是否改过。
/// 界面不认识任何具体参数含义 —— 全部来自方法声明的 <see cref="ParameterDefinition"/>。
/// </summary>
public partial class ParameterRow(ParameterDefinition definition, string hint) : ObservableObject
{
    public ParameterDefinition Definition { get; } = definition;

    public string Key => definition.Key;

    public string DisplayName => definition.DisplayName;

    public ParameterKind Kind => definition.Kind;

    public ParameterScope Scope => definition.Scope;

    public bool IsSwitch => definition.Kind == ParameterKind.Switch;

    /// <summary>数字与文本都走文本框（与开关互斥显示）。</summary>
    public bool IsTextBox => definition.Kind != ParameterKind.Switch;

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

    /// <summary>开关绑定点：值仍以 "1"/"0" 文本存储，与参数表一致。</summary>
    public bool SwitchValue
    {
        get => Value == "1";
        set => Value = value ? "1" : "0";
    }

    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(SwitchValue));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));
}

/// <summary>按**作用时点**分组的一组参数（界面按此渲染分区：运行期 / 计算期 / …）。</summary>
public sealed class ParameterGroupView(string title, string hint, IReadOnlyList<ParameterRow> rows)
{
    public string Title { get; } = title;

    public string Hint { get; } = hint;

    public IReadOnlyList<ParameterRow> Rows { get; } = rows;
}

/// <summary>
/// 参数编辑基类：按**分组**把参数分配到两个页面（试样编辑页 / 参数配置页），页内再按**作用时点**分区；
/// 负责校验、脏状态与保存闸门。界面不认识任何具体参数 —— 清单由方法通过 <see cref="ITestParameters"/> 声明。
/// </summary>
public abstract partial class ParameterEditorViewModel : ViewModelBase
{
    private readonly ITestParameters _parameters;

    /// <summary>上次载入 / 保存的值的快照（脏状态与"重新载入"用它比对）。</summary>
    private Dictionary<string, string> _saved = new(StringComparer.Ordinal);

    protected ParameterEditorViewModel(
        ILocalizationService localization,
        ITestParameters parameters,
        ParameterGroup group,
        string titleKey,
        string descriptionKey
    )
        : base(localization)
    {
        _parameters = parameters;
        Title = L(titleKey);
        Description = L(descriptionKey);

        foreach (var definition in parameters.Definitions.Where(d => d.Group == group))
        {
            Rows.Add(new ParameterRow(definition, BuildHint(definition)));
        }

        foreach (var row in Rows)
        {
            row.PropertyChanged += (_, _) => OnRowChanged(row);
        }

        foreach (
            var scope in new[]
            {
                ParameterScope.Runtime,
                ParameterScope.Computation,
                ParameterScope.Acquisition,
                ParameterScope.Presentation,
            }
        )
        {
            var rows = Rows.Where(r => r.Scope == scope).ToArray();
            if (rows.Length > 0)
            {
                Groups.Add(
                    new ParameterGroupView(L(ScopeTitleKey(scope)), L(ScopeHintKey(scope)), rows)
                );
            }
        }
    }

    /// <summary>页面标题。</summary>
    public string Title { get; }

    /// <summary>页面说明（这页参数在什么时候生效）。</summary>
    public string Description { get; }

    /// <summary>按作用时点分区（界面渲染用）。</summary>
    public ObservableCollection<ParameterGroupView> Groups { get; } = [];

    /// <summary>扁平清单（校验 / 保存 / 脏状态用）。</summary>
    public ObservableCollection<ParameterRow> Rows { get; } = [];

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>有未保存的修改。</summary>
    public bool HasChanges => Rows.Any(row => row.IsDirty);

    /// <summary>有校验错误（有错就挡住保存）。</summary>
    public bool HasErrors => Rows.Any(row => row.HasError);

    /// <summary>保存闸门：不忙、且没有校验错误。</summary>
    public bool CanSave => !IsBusy && !HasErrors;

    /// <summary>进页面时自动载入当前值。</summary>
    public override void OnNavigatedTo() => _ = ReloadAsync();

    /// <summary>从库里重新载入（有未保存修改时先确认）。</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (HasChanges && !await ConfirmAsync("Param.ReloadConfirm", Title))
        {
            return;
        }

        await LoadFromStoreAsync(L("Param.Loaded"));
    }

    /// <summary>所有参数恢复为方法声明的默认值（不落库，等用户保存；先确认）。</summary>
    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        if (!await ConfirmAsync("Param.RestoreDefaultsConfirm", Title))
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
        Status = L("Param.RestoreDefaultsDone");
    }

    /// <summary>保存到库（有校验错误时拒绝并提示）。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        RefreshValidation();

        if (HasErrors)
        {
            Status = string.Format(
                CultureInfo.CurrentCulture,
                L("Param.Invalid"),
                Rows.Count(row => row.HasError)
            );
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

            await _parameters.SaveAsync(values);
            _saved = values;
            RefreshDirty();
            Status = L("Param.Saved");
        }
        catch (Exception ex)
        {
            Status = $"{L("Param.SaveFailed")}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RaiseStateChanged();
        }
    }

    private async Task LoadFromStoreAsync(string status)
    {
        var values = await _parameters.LoadAsync();

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
        Status = status;
    }

    private void OnRowChanged(ParameterRow row)
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

    private static string ScopeTitleKey(ParameterScope scope) =>
        scope switch
        {
            ParameterScope.Computation => "Param.Scope.Computation",
            ParameterScope.Acquisition => "Param.Scope.Acquisition",
            ParameterScope.Presentation => "Param.Scope.Presentation",
            _ => "Param.Scope.Runtime",
        };

    private static string ScopeHintKey(ParameterScope scope) =>
        scope switch
        {
            ParameterScope.Computation => "Param.Scope.Computation.Hint",
            ParameterScope.Acquisition => "Param.Scope.Acquisition.Hint",
            ParameterScope.Presentation => "Param.Scope.Presentation.Hint",
            _ => "Param.Scope.Runtime.Hint",
        };

    /// <summary>确认框（重新载入 / 恢复默认这类会丢弃编辑内容的动作先问一声）。</summary>
    private async Task<bool> ConfirmAsync(string messageKey, params object[] args)
    {
        var messageBox = new Wpf.Ui.Controls.MessageBox
        {
            Title = L("Param.ConfirmTitle"),
            Content = string.Format(CultureInfo.CurrentCulture, L(messageKey), args),
            PrimaryButtonText = L("Common.Yes"),
            SecondaryButtonText = L("Common.No"),
            IsCloseButtonEnabled = false,
            Owner = System.Windows.Application.Current?.MainWindow,
        };

        return await messageBox.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }
}

/// <summary>试样编辑页：只编辑"试件尺寸"这一组参数（判定用）。</summary>
public sealed class SampleEditorViewModel(
    ILocalizationService localization,
    ITestParameters parameters
)
    : ParameterEditorViewModel(
        localization,
        parameters,
        ParameterGroup.Sample,
        "Sample.Title",
        "Sample.Description"
    );

/// <summary>参数配置页：只编辑"机器与控制"这一组参数（运行期 / 计算期）。</summary>
public sealed class MachineSettingsViewModel(
    ILocalizationService localization,
    ITestParameters parameters
)
    : ParameterEditorViewModel(
        localization,
        parameters,
        ParameterGroup.Machine,
        "Mach.Title",
        "Mach.Description"
    );
