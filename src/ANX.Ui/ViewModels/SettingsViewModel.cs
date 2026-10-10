namespace ANX.Ui.ViewModels;

using System.Diagnostics;
using System.Reflection;
using ANX.Ui.Services;

/// <summary>一个主题选项。</summary>
public sealed record ThemeOption(string Key, string DisplayName);

/// <summary>系统设置页 ViewModel：语言、主题、日志目录、版本信息。</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IThemeService _theme;
    private readonly ILogDirectoryProvider _logDirectoryProvider;
    private bool _isInitialized;

    public SettingsViewModel(
        ILocalizationService localization,
        IThemeService theme,
        ILogDirectoryProvider logDirectoryProvider
    )
        : base(localization)
    {
        _theme = theme;
        _logDirectoryProvider = logDirectoryProvider;

        localization.LanguageChanged += (_, _) => RebuildThemeOptions();
        RebuildThemeOptions();
    }

    public IReadOnlyList<LanguageInfo> Languages => Localization.Languages;

    public ObservableCollection<ThemeOption> ThemeOptions { get; } = [];

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

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
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value is null || string.Equals(value.Key, _theme.CurrentTheme, StringComparison.Ordinal))
        {
            return;
        }

        _theme.Apply(value.Key);
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
}
