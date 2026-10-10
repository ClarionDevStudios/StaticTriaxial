namespace ANX.Host;

using System.IO;

/// <summary>引导层路径。M1 只用到日志目录；将来数据库路径、首次启动标记也归这里。</summary>
internal static class AppPaths
{
    /// <summary>日志目录：程序目录下的 Logs\。</summary>
    public static string ResolveLogDirectory() => Path.Combine(AppContext.BaseDirectory, "Logs");

    /// <summary>数据库：程序目录下的 Bins\Data\anx.db（沿用现役的 Bins\ 产物约定）。</summary>
    public static string ResolveDatabasePath() =>
        Path.Combine(AppContext.BaseDirectory, "Bins", "Data", "anx.db");
}
