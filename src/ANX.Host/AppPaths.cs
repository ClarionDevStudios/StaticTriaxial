namespace ANX.Host;

using System.IO;

/// <summary>引导层路径。M1 只用到日志目录；将来数据库路径、首次启动标记也归这里。</summary>
internal static class AppPaths
{
    /// <summary>日志文件名前缀（Serilog 按天滚动时会在中间插入日期）。</summary>
    private const string LogFilePrefix = "anx-";

    private const string LogFileExtension = ".log";

    /// <summary>日志目录：程序目录下的 Logs\。</summary>
    public static string ResolveLogDirectory() => Path.Combine(AppContext.BaseDirectory, "Logs");

    /// <summary>
    /// Serilog 文件汇的路径模板：`Logs\anx-.log`。结尾的 `-` 是滚动标记，
    /// Serilog 会在中间插入日期，写成 `anx-20261011.log`。
    /// </summary>
    public static string ResolveLogSinkPath() =>
        Path.Combine(ResolveLogDirectory(), LogFilePrefix + LogFileExtension);

    /// <summary>
    /// 当天那个日志文件的实际路径（与 <see cref="ResolveLogSinkPath"/> 的滚动规则一致）。
    /// 失败提示框要告诉用户"日志在哪"，所以命名规则只在这里定义一份。
    /// </summary>
    public static string ResolveCurrentLogFile() =>
        Path.Combine(ResolveLogDirectory(), $"{LogFilePrefix}{DateTime.Now:yyyyMMdd}{LogFileExtension}");

    /// <summary>数据库：程序目录下的 Bins\Data\anx.db（沿用现役的 Bins\ 产物约定）。</summary>
    public static string ResolveDatabasePath() =>
        Path.Combine(AppContext.BaseDirectory, "Bins", "Data", "anx.db");

    /// <summary>报表与曲线图产物根：程序目录下的 Bins\Report（沿用现役约定）。</summary>
    public static string ResolveReportRoot() =>
        Path.Combine(AppContext.BaseDirectory, "Bins", "Report");
}
