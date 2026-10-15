namespace ANX.Ui.ViewModels;

using ANX.Control;
using ANX.Core.Control;
using ANX.Ui.Services;

/// <summary>按**作用时点**分组的一组方法参数（界面按此渲染分区：运行期 / 计算期 / …）。</summary>
public sealed class ParameterGroupView(string title, string hint, IReadOnlyList<ParameterRow> rows)
{
    public string Title { get; } = title;

    public string Hint { get; } = hint;

    public IReadOnlyList<ParameterRow> Rows { get; } = rows;
}

/// <summary>一个页签（比照现役配置窗体的 PageControl：通道设置 / 试验参数）。</summary>
public partial class ParameterTabView(string title, IReadOnlyList<ParameterGroupView> groups)
    : ObservableObject
{
    public string Title { get; } = title;

    public IReadOnlyList<ParameterGroupView> Groups { get; } = groups;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// **方法参数**编辑页基类：按分组（机器 / 试件）分页、页内按作用时点分区，读写方法参数表。
/// 试件尺寸**不属于这里** —— 它是试样信息（见 <see cref="SampleInfoViewModel"/>，用户 2026-10-12 明确）。
///
/// 用户 2026-10-13 起本页以**弹出窗体**打开（见 <see cref="IModalPageService"/>），
/// 所以底部给了一个「关闭」—— 以前靠导航栏离开，现在得让窗体自己关得掉。
/// </summary>
public abstract partial class ParameterEditorViewModel : EditableRowsViewModel, IModalDialogViewModel
{
    private readonly ITestParameters _parameters;
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    protected ParameterEditorViewModel(
        ILocalizationService localization,
        ITestParameters parameters,
        IEnumerable<ParameterDefinition> definitions,
        ParameterGroup group,
        string titleKey,
        string descriptionKey
    )
        : base(localization)
    {
        _parameters = parameters;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;

        InitializeRows(definitions.Where(d => d.Group == group));

        // ⚠ 构造里**不能**调 RefreshTexts()（它会调到子类覆盖的 BuildDescription）：
        // 子类的字段此刻还没赋值 —— MachineSettingsViewModel 就是这么炸的
        // （2026-10-15 用户点「试验参数」报 NullReferenceException，堆栈正是 BuildDescription→RefreshTexts→ctor）。
        // 所以构造里只填**不依赖子类**的部分，描述用基类自己的兜底文字；
        // 子类在自己的构造末尾调一次 RefreshTexts()，把带方法名的那种描述补上。
        Title = L(_titleKey);
        Description = L(_descriptionKey);
        RebuildTabs();

        // 换语言：**页级文字也要跟着变**（用户 2026-10-15：切换语言后参数行变了，
        // 但页标题/页签/分区标题还是旧语言）。它们是构造时算一次就定下来的，得整个重建。
        localization.LanguageChanged += (_, _) => RefreshTexts();
    }

    /// <summary>页签（比照现役配置窗体）：通道设置 + 试验参数。</summary>
    public ObservableCollection<ParameterTabView> Tabs { get; } = [];

    /// <summary>
    /// 按当前语言重建页级文字：页标题、页描述、页签标题、分区（卡片）标题与说明。
    /// 参数行由 <see cref="EditableRowsViewModel"/> 自己通知，这里不管。
    /// </summary>
    public void RefreshTexts()
    {
        Title = L(_titleKey);
        Description = BuildDescription();
        RebuildTabs();
    }

    /// <summary>页签与分区（卡片）按当前语言重建，保留选中的那个页签。</summary>
    private void RebuildTabs()
    {
        var selected = SelectedTab is null ? 0 : Tabs.IndexOf(SelectedTab);

        Tabs.Clear();
        // 页签 1：通道设置（采集口径）
        AddTab("Mach.Tab.Channels", [ParameterScope.Acquisition]);
        // 页签 2：试验参数（运行期 / 计算期 / 记录与显示）
        AddTab(
            "Mach.Tab.Parameters",
            [ParameterScope.Runtime, ParameterScope.Computation, ParameterScope.Presentation]
        );

        SelectTab(
            Tabs.Count == 0 ? null : Tabs[Math.Clamp(selected, 0, Tabs.Count - 1)]
        );
    }

    /// <summary>页描述：默认取声明给的键；子类可覆盖成"带方法名"的那种（见 <see cref="MachineSettingsViewModel"/>）。</summary>
    protected virtual string BuildDescription() => L(_descriptionKey);

    private void AddTab(string titleKey, IReadOnlyList<ParameterScope> scopes)
    {
        var groups = BuildGroups(scopes);
        if (groups.Count > 0)
        {
            Tabs.Add(new ParameterTabView(L(titleKey), groups));
        }
    }

    /// <inheritdoc />
    public event EventHandler<bool>? CloseRequested;

    /// <summary>关闭弹窗（参数改动只有点了「保存」才落地，所以这里不给"确定"语义）。</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, false);

    [ObservableProperty]
    private ParameterTabView? _selectedTab;

    /// <summary>切页签。</summary>
    [RelayCommand]
    private void SelectTab(ParameterTabView? tab)
    {
        if (tab is null || ReferenceEquals(tab, SelectedTab))
        {
            return;
        }

        foreach (var item in Tabs)
        {
            item.IsSelected = ReferenceEquals(item, tab);
        }

        SelectedTab = tab;
    }

    /// <summary>把行按作用时点分组成若干分区（只保留该页签关心的时点、且非空的分区）。</summary>
    private List<ParameterGroupView> BuildGroups(IReadOnlyList<ParameterScope> scopes)
    {
        var groups = new List<ParameterGroupView>();

        foreach (var scope in scopes)
        {
            var rows = OrderRows(scope, Rows.Where(r => r.Scope == scope).ToArray());
            if (rows.Length > 0)
            {
                groups.Add(
                    new ParameterGroupView(L(ScopeTitleKey(scope)), L(ScopeHintKey(scope)), rows)
                );
            }
        }

        return groups;
    }

    /// <summary>
    /// 行内显示顺序：运行期按通用目录给的顺序表排（用户 2026-10-12 指定：平台升降速率 → 试验电机速率 →
    /// 两个闸门系数 → 接触判定阈值 → 不判破坏下限 → 破坏判据 → 破坏确认帧数 → 破坏后继续加载时间 →
    /// 试验完成后归位时间），不在表里的排在末尾。
    /// </summary>
    private static ParameterRow[] OrderRows(ParameterScope scope, ParameterRow[] rows)
    {
        if (scope != ParameterScope.Runtime)
        {
            return rows;
        }

        var order = MachineParameterCatalog.RuntimeOrderKeys;

        return
        [
            .. rows.OrderBy(row =>
            {
                var index = -1;
                for (var i = 0; i < order.Count; i++)
                {
                    if (string.Equals(order[i], row.Key, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }

                return index < 0 ? int.MaxValue : index;
            }),
        ];
    }

    /// <summary>
    /// 打开时自动载入当前值（导航页与弹窗都走这里，用户 2026-10-13）。
    /// 同时**按当前语言重刷一遍页级文字** —— ViewModel 是单例，上次打开时算好的标题/页签
    /// 在换过语言之后还是旧语言（用户 2026-10-15）。
    /// </summary>
    public override void OnOpened()
    {
        RefreshTexts();
        _ = ReloadFromStoreAsync();
    }

    private async Task ReloadFromStoreAsync()
    {
        ApplyValues(await LoadCoreAsync());
        SetStatus(L("Param.Loaded"));
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

/// <summary>
/// 参数配置页（**通用界面**，比照现役配置窗体）：渲染"<see cref="MachineParameterCatalog"/> 通用参数
/// ∪ 当前方法声明的参数"（同键以方法为准），值按方法代号存库。
/// 通道设置保存后**即时推给设备适配器** —— 荷载/位移读数立刻按新通道取（用户 2026-10-12 明确）。
/// </summary>
public sealed partial class MachineSettingsViewModel : ParameterEditorViewModel
{
    private readonly ITestDeviceController? _device;
    private readonly ITestExecution _execution;

    public MachineSettingsViewModel(
        ILocalizationService localization,
        ITestParameters parameters,
        ITestExecution execution,
        ITestDeviceController? device
    )
        : base(
            localization,
            parameters,
            MergeDefinitions(parameters),
            ParameterGroup.Machine,
            "Mach.Title",
            "Mach.Description"
        )
    {
        _device = device;
        _execution = execution;

        // 字段都到齐了才刷描述（构造里那次用的是基类兜底描述）
        RefreshTexts();
    }

    /// <summary>
    /// 通用界面：描述里写明**当前试验方法**（同名窗体、值按方法代号存）。
    /// 做成方法而不是构造里赋一次 —— 换语言时要能按新语言重新拼一遍（含方法名）。
    /// </summary>
    protected override string BuildDescription() =>
        string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            L("Mach.CurrentMethod"),
            L(_execution.MethodNameKey),
            _execution.MethodCode
        );

    /// <summary>通用目录在前、方法声明在后 → 同键最后取到的是方法声明（方法优先）。</summary>
    private static IEnumerable<ParameterDefinition> MergeDefinitions(ITestParameters parameters) =>
        MachineParameterCatalog
            .Definitions.Concat(parameters.Definitions)
            .GroupBy(definition => definition.Key, StringComparer.Ordinal)
            .Select(group => group.Last());

    /// <summary>保存成功后把通道设置立刻推给设备（通道选择即时生效）。</summary>
    protected override void AfterSave(IReadOnlyDictionary<string, string> values)
    {
        if (_device is null)
        {
            return;
        }

        // 下拉里的值是现役口径的 0 基编号；设备适配器要的是协议通道号（1 基）
        if (TryGetChoice(values, MachineParameterCatalog.PressChannelKey, out var press))
        {
            _device.PressChannelNumber = press + 1;
        }

        // 位移来源：单路（通道 / 千分表）与四组双通道平均都支持 —— 双传感器 = 两路取平均
        if (TryGetChoice(values, MachineParameterCatalog.DisplaceChannelKey, out var displace))
        {
            _device.DisplacementSource = DisplacementSources.Parse(displace);
        }
    }

    private static bool TryGetChoice(
        IReadOnlyDictionary<string, string> values,
        string key,
        out int choice
    )
    {
        choice = 0;
        return values.TryGetValue(key, out var text)
            && int.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out choice
            );
    }
}
