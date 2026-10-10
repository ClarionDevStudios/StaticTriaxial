namespace ANX.Host;

using ANX.Ui.Services;

/// <inheritdoc />
internal sealed class AppLogDirectoryProvider(string logDirectory) : ILogDirectoryProvider
{
    public string GetLogDirectory() => logDirectory;
}
