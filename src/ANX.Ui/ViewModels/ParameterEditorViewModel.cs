namespace ANX.Ui.ViewModels;

using ANX.Control;
using ANX.Ui.Services;

/// <summary>按**作用时点**分组的一组方法参数（界面按此渲染分区：运行期 / 计算期 / …）。</summary>
public sealed class ParameterGroupView(string title, string hint, IReadOnlyList<ParameterRow> rows)
{
    public string Title { get; } = title;

    public string Hint { get; } = hint;

    public IReadOnlyList<ParameterRow> Rows { get; } = rows;
}

/// <summary>
/// **方法参数**编辑页基类：按分组（机器 / 试件）分页、页内按作用时点分区，读写方法参数表。
/// 试件尺寸**不属于这里** —— 它是试样信息（见 <see cref="SampleInfoViewModel"/>，用户 2026-10-12 明确）。
/// </summary>
public abstract class ParameterEditorViewModel : EditableRowsViewModel
{
    private readonly ITestParameters _parameters;

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

        InitializeRows(parameters.Definitions.Where(d => d.Group == group));

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

    /// <summary>按作用时点分区（界面渲染用）。</summary>
    public ObservableCollection<ParameterGroupView> Groups { get; } = [];

    /// <summary>进页面时自动载入当前值。</summary>
    public override void OnNavigatedTo() => _ = ReloadFromStoreAsync();

    private async Task ReloadFromStoreAsync()
    {
        ApplyValues(await LoadCoreAsync());
        Status = L("Param.Loaded");
    }

    protected override Task<IReadOnlyDictionary<string, string>> LoadCoreAsync() =>
        _parameters.LoadAsync();

    protected override Task SaveCoreAsync(IReadOnlyDictionary<string, string> values) =>
        _parameters.SaveAsync(values);

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
}

/// <summary>参数配置页：只编辑"机器与控制"这一组方法参数（运行期 / 计算期）。</summary>
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
