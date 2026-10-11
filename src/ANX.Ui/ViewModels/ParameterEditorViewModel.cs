namespace ANX.Ui.ViewModels;

using ANX.Control;
using ANX.Ui.Services;

/// <summary>一行参数编辑（键 + 显示名 + 单位 + 值）。</summary>
public partial class ParameterRow(ParameterDefinition definition) : ObservableObject
{
    public string Key { get; } = definition.Key;

    public string DisplayName { get; } = definition.DisplayName;

    public string Unit { get; } = definition.Unit;

    [ObservableProperty]
    private string _value = definition.DefaultValue;
}

/// <summary>
/// 参数编辑基类：按**分组**把参数分配到两个页面（试样编辑页 / 参数配置页），
/// 界面不认识任何具体参数含义 —— 参数清单由方法通过 <see cref="ITestParameters"/> 声明。
/// </summary>
public abstract partial class ParameterEditorViewModel : ViewModelBase
{
    private readonly ITestParameters _parameters;
    private readonly ParameterGroup _group;

    protected ParameterEditorViewModel(
        ILocalizationService localization,
        ITestParameters parameters,
        ParameterGroup group,
        string titleKey
    )
        : base(localization)
    {
        _parameters = parameters;
        _group = group;
        Title = L(titleKey);

        foreach (var definition in parameters.Definitions.Where(d => d.Group == group))
        {
            Rows.Add(new ParameterRow(definition));
        }
    }

    /// <summary>页面标题。</summary>
    public string Title { get; }

    public ObservableCollection<ParameterRow> Rows { get; } = [];

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>进页面时自动载入当前值。</summary>
    public override void OnNavigatedTo() => _ = ReloadAsync();

    /// <summary>重新从库里载入。</summary>
    [RelayCommand]
    private async Task ReloadAsync()
    {
        var values = await _parameters.LoadAsync();

        foreach (var row in Rows)
        {
            row.Value = values.TryGetValue(row.Key, out var value) ? value : row.Value;
        }

        Status = L("Param.Loaded");
    }

    /// <summary>保存到库。</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var values = Rows.ToDictionary(
                row => row.Key,
                row => row.Value ?? string.Empty,
                StringComparer.Ordinal
            );

            await _parameters.SaveAsync(values);
            Status = L("Param.Saved");
        }
        catch (Exception ex)
        {
            Status = $"{L("Param.SaveFailed")}: {ex.Message}";
        }
    }
}

/// <summary>试样编辑页：只编辑"试件尺寸"这一组参数。</summary>
public sealed class SampleEditorViewModel(
    ILocalizationService localization,
    ITestParameters parameters
) : ParameterEditorViewModel(localization, parameters, ParameterGroup.Sample, "Sample.Title");

/// <summary>参数配置页：只编辑"机器与控制"这一组参数。</summary>
public sealed class MachineSettingsViewModel(
    ILocalizationService localization,
    ITestParameters parameters
) : ParameterEditorViewModel(localization, parameters, ParameterGroup.Machine, "Mach.Title");
