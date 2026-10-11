namespace ANX.Ui.ViewModels;

using ANX.Control;
using ANX.Core.TestSheets;
using ANX.Ui.Services;
using Wpf.Ui;

/// <summary>
/// 试验单页：顶部 RadioButton 切"新建试验单 / 选择已有试验单"，中部随模式切换，下部确定 / 取消。
/// 确定后把选定的单交给 <see cref="ICurrentSheetContext"/>，再导航到试验执行页。
/// </summary>
public partial class SheetSelectionViewModel : ViewModelBase
{
    private readonly ITestExecution _execution;
    private readonly ITestSheetBrowser _browser;
    private readonly ICurrentSheetContext _current;
    private readonly INavigationService _navigation;

    public SheetSelectionViewModel(
        ILocalizationService localization,
        ITestExecution execution,
        ITestSheetBrowser browser,
        ICurrentSheetContext current,
        INavigationService navigation
    )
        : base(localization)
    {
        _execution = execution;
        _browser = browser;
        _current = current;
        _navigation = navigation;
        Status = L("Sheet.Ready");
    }

    /// <summary>true = 新建试验单；false = 选择已有试验单。</summary>
    [ObservableProperty]
    private bool _isNewSheet = true;

    /// <summary>与 <see cref="IsNewSheet"/> 互补，供第二个面板绑定可见性。</summary>
    [ObservableProperty]
    private bool _isExistingSheet;

    [ObservableProperty]
    private string _projectName = string.Empty;

    [ObservableProperty]
    private string _numberPrefix = "WT";

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private TestSheetSummary? _selectedExisting;

    /// <summary>已有试验单列表。</summary>
    public ObservableCollection<TestSheetSummary> ExistingSheets { get; } = [];

    partial void OnIsNewSheetChanged(bool value) => IsExistingSheet = !value;

    /// <summary>读取已有试验单列表。</summary>
    [RelayCommand]
    private async Task LoadExistingAsync()
    {
        ExistingSheets.Clear();

        var sheets = await _browser.QueryAsync(null);
        foreach (var sheet in sheets)
        {
            ExistingSheets.Add(sheet);
        }

        Status = $"{L("Sheet.Loaded")} {ExistingSheets.Count}";
        SelectedExisting = ExistingSheets.FirstOrDefault();
    }

    /// <summary>确定：新建或采用选中单，然后进执行页。</summary>
    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (IsNewSheet)
        {
            var sheetId = await _execution.CreateSheetAsync(ProjectName, NumberPrefix);
            _current.Set(sheetId, ProjectName);
            Status = $"{L("Sheet.Created")} {sheetId}";
        }
        else
        {
            if (SelectedExisting is null)
            {
                Status = L("Sheet.SelectFirst");
                return;
            }

            _current.Set(SelectedExisting.SheetId, SelectedExisting.ProjectName);
            Status = $"{L("Sheet.Selected")} {SelectedExisting.SheetId}";
        }

        _navigation.Navigate(typeof(Views.Pages.TestExecutionPage));
    }

    /// <summary>取消：回到首页。</summary>
    [RelayCommand]
    private void Cancel() => _navigation.Navigate(typeof(Views.Pages.HomePage));
}
