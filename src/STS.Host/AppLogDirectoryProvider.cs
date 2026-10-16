namespace STS.Host;

using STS.Ui.Services;

/// <inheritdoc />
internal sealed class AppLogDirectoryProvider(string logDirectory) : ILogDirectoryProvider
{
    public string GetLogDirectory() => logDirectory;
}
