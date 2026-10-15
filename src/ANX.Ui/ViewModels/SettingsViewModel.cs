namespace ANX.Ui.ViewModels;

using System.Diagnostics;
using System.Reflection;
using ANX.Core.Control;
using ANX.Core.Settings;
using ANX.Ui.Services;

/// <summary>一个主题选项。</summary>
public sealed record ThemeOption(string Key, string DisplayName);

/// <summary>系统设置页 ViewModel：语言、主题、数据来源、日志目录、版本信息。</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService _theme;
    private readonly ILogDirectoryProvider _logDirectoryProvider;
    private readonly IAppSettingsService? _settings;
    private readonly IDeviceSourceSwitch? _deviceSource;
    private bool _isInitialized;
    private bool _loadingDataSource;
    private string _dataSourceKey = AppDataSources.Device;

    public SettingsViewModel(
        ILocalizationService localization,
        IThemeService theme,
        ILogDirectoryProvider logDirectoryProvider,
        IAppSettingsService? settings = null,
        IDeviceSourceSwitch? deviceSource = null
    )
        : base(localization)
    {
        _theme = theme;
        _logDirectoryProvider = logDirectoryProvider;
        _settings = settings;
        _deviceSource = deviceSource;

        localization.LanguageChanged += (_, _) => RebuildThemeOptions();
        RebuildThemeOptions();
    }

    public IReadOnlyList<LanguageInfo> Languages => Localization.Languages;

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = [];

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    /// <summary>
    /// 数据来源：true = 模拟数据（模拟器产生试验数据），false = 真机试验。
    /// 与 <see cref="IsDeviceSource"/> 是一对单选 —— 两个 RadioButton 各绑一边，不用转换器。
    /// </summary>
    [ObservableProperty]
    private bool _isSimulatorSource;

    /// <summary>数据来源 = 真机试验（与 <see cref="IsSimulatorSource"/> 互斥）。</summary>
    public bool IsDeviceSource
    {
        get => !IsSimulatorSource;
        set
        {
            if (value && IsSimulatorSource)
            {
                IsSimulatorSource = false; // 由下面那个 partial 方法负责落盘与重新装配
            }
        }
    }

    partial void OnIsSimulatorSourceChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDeviceSource)); // "真机试验"那半边跟着刷新
        SwitchDataSource(value ? AppDataSources.Simulator : AppDataSources.Device);
    }

    /// <summary>
    /// 换数据来源（用户 2026-10-15）：落盘 + **立刻**让设备侧按新来源重新装配并联机
    ///（不必重启软件；界面随后那句状态会说明结果）。
    /// </summary>
    private void SwitchDataSource(string key)
    {
        if (_loadingDataSource || string.Equals(key, _dataSourceKey, StringComparison.Ordinal))
        {
            return;
        }

        _dataSourceKey = key;
        Persist(AppSettingKeys.DataSource, key);
        _ = ApplyDataSourceAsync(key);
    }

    private async Task ApplyDataSourceAsync(string key)
    {
        if (_deviceSource is null)
        {
            return;
        }

        var name = L(
            string.Equals(key, AppDataSources.Simulator, StringComparison.Ordinal)
                ? "Settings.DataSourceSimulator"
                : "Settings.DataSourceDevice"
        );

        try
        {
            await _deviceSource.ApplyAsync().ConfigureAwait(true);
            SetStatus($"{L("Settings.DataSourceSwitched")}：{name}");
        }
        catch (Exception ex)
        {
            SetStatus($"{L("Settings.DataSourceFailed")}: {ex.Message}", isError: true);
        }
    }

    [ObservableProperty]
    private string _logDirectory = string.Empty;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _runtimeVersion = string.Empty;

    [ObservableProperty]
    private string _uiLibraryVersion = string.Empty;

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        if (value is null || string.Equals(value.Code, Localization.CurrentCulture, StringComparison.Ordinal))
        {
            return;
        }

        Localization.SetLanguage(value.Code);
        Persist(AppSettingKeys.Language, value.Code);
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null || string.Equals(value.Key, _theme.CurrentTheme, StringComparison.Ordinal))
        {
            return;
        }

        _theme.Apply(value.Key);
        Persist(AppSettingKeys.Theme, value.Key);
    }

    /// <summary>
    /// 界面一改就写进**外部配置文件**（用户 2026-10-14：改完下次启动要生效）。
    ///
    /// 这里是"立即生效 + 立即落盘"（没有确定/取消按钮），所以成功与否都要给一句话 ——
    /// 用户报的正是"改了下次启动不生效"，看不到确认就无从判断。写失败按错误显示（红字闪烁）。
    /// </summary>
    private void Persist(string key, string value)
    {
        if (_settings is null)
        {
            return;
        }

        _ = PersistAsync(key, value);
    }

    private async Task PersistAsync(string key, string value)
    {
        try
        {
            await _settings!.SetAsync(key, value).ConfigureAwait(true);
            SetStatus($"{L("Settings.Saved")}（{L("Settings.SavedNextStart")}）");
        }
        catch (Exception ex)
        {
            SetStatus($"{L("Settings.SaveFailed")}: {ex.Message}", isError: true);
        }
    }

    /// <summary>页面构造时调用一次，装载只读信息。</summary>
    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        LogDirectory = _logDirectoryProvider.GetLogDirectory();

        var assembly = Assembly.GetExecutingAssembly();
        AppVersion = assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        RuntimeVersion = Environment.Version.ToString();
        UiLibraryVersion =
            typeof(Wpf.Ui.Controls.FluentWindow).Assembly.GetName().Version?.ToString(3) ?? "4.x";

        SelectedLanguage =
            Languages.FirstOrDefault(l => l.Code == Localization.CurrentCulture) ?? Languages[0];
        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Key == _theme.CurrentTheme);

        // 数据来源要读配置文件（异步），页面构造是同步的 —— 起个头，读完再选上
        _ = LoadDataSourceAsync();

        _isInitialized = true;
    }

    [RelayCommand]
    private void OpenLogDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LogDirectory}\""));
        }
        catch (Exception)
        {
            // 某些环境没有资源管理器 —— 打不开就静默放过，M1 不为此加提示通道。
        }
    }

    private void RebuildThemeOptions()
    {
        ThemeOptions.Clear();
        ThemeOptions.Add(new ThemeOption("Dark", L("Settings.ThemeDark")));
        ThemeOptions.Add(new ThemeOption("Light", L("Settings.ThemeLight")));

        SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Key == _theme.CurrentTheme);
    }

    /// <summary>
    /// 装载**当前**数据来源（读配置文件；没配过就是真机试验）。
    /// 页面构造是同步的，所以这里只起个头，读完再选上 —— 载入期间不落盘、不重新装配设备。
    /// </summary>
    private async Task LoadDataSourceAsync()
    {
        if (_settings is null)
        {
            return;
        }

        string? stored = null;
        try
        {
            stored = await _settings.GetAsync(AppSettingKeys.DataSource).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // 读不到就按默认（真机试验）
        }

        var key = string.Equals(stored, AppDataSources.Simulator, StringComparison.Ordinal)
            ? AppDataSources.Simulator
            : AppDataSources.Device;

        _loadingDataSource = true;
        try
        {
            _dataSourceKey = key;
            IsSimulatorSource = string.Equals(key, AppDataSources.Simulator, StringComparison.Ordinal);
        }
        finally
        {
            _loadingDataSource = false;
        }
    }
}

