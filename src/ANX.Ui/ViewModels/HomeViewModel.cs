namespace ANX.Ui.ViewModels;

using System.Collections.ObjectModel;
using ANX.Control.Methods;
using ANX.Ui.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui;

/// <summary>首页上的一行里程碑。</summary>
public sealed record MilestoneItem(string Code, string Description, string StatusText, bool IsDone);

/// <summary>
/// 首页上的一张**试验方法卡片**（方案 A：首页当"方法入口"，用户 2026-10-16 定的）。
/// <paramref name="MarkerText"/> = "当前"标记（不是当前方法时为空串）—— 界面不用转换器，直接绑文本。
/// </summary>
public sealed record MethodCardItem(
    string Code,
    string Name,
    string StandardReference,
    bool IsCurrent,
    string MarkerText
);

/// <summary>
/// 首页 ViewModel：**试验方法入口卡片**（点哪张进哪张的方法）+ 里程碑进度 + 已知边界。
///
/// 方法卡片是唯一事实来源 <see cref="ICurrentMethodContext"/> 的呈现：点卡片 = 切到该方法
/// （页面持有的五件套是"当前方法代理"，见 <c>CurrentMethodExecution</c> 等）→ 进试验执行页。
/// 各页在方法切换后自己把按方法算出来的东西重建（卡片、列、单号…）。
///
/// **依赖注意**：执行页 ViewModel（<see cref="TestExecutionViewModel"/>）用 **<see cref="Func{TResult}"/> 懒取** ——
/// 首页只在一件事上用得到它（"试验进行中不让切方法"），而它自己又拖着一长串依赖（设备、弹窗、试样信息…）。
/// 直接在构造里注入它，会让"首页一显示"就把整条执行链拉起来（实测会把启动导航卡住）。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly ICurrentMethodContext _methods;
    private readonly Func<TestExecutionViewModel>? _execution;
    private readonly INavigationService? _navigation;

    /// <summary>
    /// 里程碑定义 —— **首页进度只读这一处**：做完一个就把 IsDone 改成 true，
    /// 不要在 XAML 里另写一份状态。
    /// </summary>
    private static readonly (string Code, string DescriptionKey, bool IsDone)[] Definitions =
    [
        ("M1", "Home.M1", true),
        ("M2", "Home.M2", true),
        ("M3", "Home.M3", true),
        ("M4", "Home.M4", true),
    ];

    public HomeViewModel(
        ILocalizationService localization,
        ICurrentMethodContext methods,
        INavigationService? navigation = null,
        Func<TestExecutionViewModel>? execution = null
    )
        : base(localization)
    {
        _methods = methods;
        _navigation = navigation;
        _execution = execution;

        localization.LanguageChanged += (_, _) =>
        {
            RebuildMilestones();
            RebuildMethods(); // 方法名与"当前"标记都是按语言现取的
        };

        // 在别处切了方法（例如以后设置页也放一个入口）也要把卡片刷新
        methods.Changed += (_, _) => RebuildMethods();

        RebuildMilestones();
        RebuildMethods();
    }

    /// <summary>里程碑列表（文案随语言切换重建）。</summary>
    public ObservableCollection<MilestoneItem> Milestones { get; } = [];

    /// <summary>试验方法卡片（顺序 = 方法目录的登记顺序）。</summary>
    public ObservableCollection<MethodCardItem> MethodCards { get; } = [];

    [ObservableProperty]
    private string _currentMilestoneText = string.Empty;

    /// <summary>
    /// 点一张方法卡片：**切到该方法**（已经是当前方法就只导航）→ 进试验执行页。
    ///
    /// 试验进行中不让切：设备与阶段机都按**每方法一套参数**装配，跑到一半换方法会把这一轮弄乱；
    /// 拦下来给一句提示。
    /// </summary>
    [RelayCommand]
    private async Task EnterMethodAsync(MethodCardItem? card)
    {
        if (card is null)
        {
            return;
        }

        if (!card.IsCurrent && _execution?.Invoke().IsRunning == true)
        {
            await AppDialogs.NotifyAsync(
                L("Home.MethodsTitle"),
                L("Home.MethodSwitchBlocked"),
                L("Common.Close")
            );
            return;
        }

        if (!card.IsCurrent)
        {
            _methods.Select(card.Code);
        }

        _navigation?.Navigate(typeof(Views.Pages.TestExecutionPage));
    }

    /// <summary>回到首页时刷新一遍"当前方法"标记（在别处切过方法也照得住）。</summary>
    public override void OnNavigatedTo() => RebuildMethods();

    private void RebuildMilestones()
    {
        Milestones.Clear();

        foreach (var (code, descriptionKey, isDone) in Definitions)
        {
            Milestones.Add(
                new MilestoneItem(
                    code,
                    L(descriptionKey),
                    L(isDone ? "Home.StatusDone" : "Home.StatusTodo"),
                    isDone
                )
            );
        }

        var lastDone = Array.FindLast(Definitions, d => d.IsDone);
        CurrentMilestoneText = $"{L("Home.CurrentMilestone")} {lastDone.Code}";
    }

    private void RebuildMethods()
    {
        MethodCards.Clear();

        foreach (var method in _methods.Methods)
        {
            var isCurrent = string.Equals(
                method.MethodCode,
                _methods.Current.MethodCode,
                StringComparison.Ordinal
            );

            MethodCards.Add(
                new MethodCardItem(
                    method.MethodCode,
                    L(method.MethodNameKey),
                    method.Execution.StandardReference,
                    isCurrent,
                    isCurrent ? L("Home.MethodCurrent") : string.Empty
                )
            );
        }
    }
}
