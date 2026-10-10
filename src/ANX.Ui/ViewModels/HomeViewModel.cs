namespace ANX.Ui.ViewModels;

using ANX.Ui.Services;

/// <summary>首页上的一行里程碑。</summary>
public sealed record MilestoneItem(string Code, string Description, string StatusText, bool IsDone);

/// <summary>首页 ViewModel：里程碑进度与已知边界。</summary>
public partial class HomeViewModel : ViewModelBase
{
    /// <summary>
    /// 里程碑定义 —— **首页进度只读这一处**：做完一个就把 IsDone 改成 true，
    /// 不要在 XAML 里另写一份状态。
    /// </summary>
    private static readonly (string Code, string DescriptionKey, bool IsDone)[] Definitions =
    [
        ("M1", "Home.M1", true),
        ("M2", "Home.M2", true),
        ("M3", "Home.M3", false),
        ("M4", "Home.M4", false),
    ];

    public HomeViewModel(ILocalizationService localization)
        : base(localization)
    {
        localization.LanguageChanged += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>里程碑列表（文案随语言切换重建）。</summary>
    public ObservableCollection<MilestoneItem> Milestones { get; } = [];

    [ObservableProperty]
    private string _currentMilestoneText = string.Empty;

    private void Rebuild()
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
}
