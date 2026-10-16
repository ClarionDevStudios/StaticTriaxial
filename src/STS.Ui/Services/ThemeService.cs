namespace STS.Ui.Services;

using Wpf.Ui.Appearance;

/// <inheritdoc />
public sealed class ThemeService : IThemeService
{
    public string CurrentTheme { get; private set; } = "Dark";

    public event EventHandler<string>? ThemeChanged;

    public void Apply(string themeName)
    {
        var isDark = !string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase);
        var name = isDark ? "Dark" : "Light";

        if (Application.Current is null)
        {
            CurrentTheme = name;
            return;
        }

        ApplicationThemeManager.Apply(isDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        CurrentTheme = name;

        ThemeChanged?.Invoke(this, name);
    }
}
