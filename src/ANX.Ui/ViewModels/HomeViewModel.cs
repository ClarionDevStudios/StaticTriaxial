namespace ANX.Ui.ViewModels;

using ANX.Ui.Services;

/// <summary>首页 ViewModel。M1 阶段只展示骨架状态与已知边界。</summary>
public partial class HomeViewModel : ViewModelBase
{
    public HomeViewModel(ILocalizationService localization)
        : base(localization) { }

    /// <summary>本工程当前所处里程碑（M1）。</summary>
    public string CurrentMilestone => "M1";
}
