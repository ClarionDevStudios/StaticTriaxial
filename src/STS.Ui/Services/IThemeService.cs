namespace STS.Ui.Services;

/// <summary>界面主题（深浅两档）。</summary>
public interface IThemeService
{
    /// <summary>当前主题名（Dark / Light）。</summary>
    string CurrentTheme { get; }

    /// <summary>主题已切换。</summary>
    event EventHandler<string>? ThemeChanged;

    /// <summary>应用主题。</summary>
    void Apply(string themeName);
}
