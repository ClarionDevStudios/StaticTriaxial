namespace STS.Host;

using System.IO;

/// <summary>引导层路径。M1 只用到日志目录；将来数据库路径、首次启动标记也归这里。</summary>
internal static class AppPaths
{
    /// <summary>日志文件名前缀（Serilog 按天滚动时会在中间插入日期）。</summary>
    private const string LogFilePrefix = "sts-";

    private const string LogFileExtension = ".log";

    /// <summary>日志目录：程序目录下的 Logs\。</summary>
    public static string ResolveLogDirectory() => Path.Combine(AppContext.BaseDirectory, "Logs");

    /// <summary>
    /// Serilog 文件汇的路径模板：`Logs\sts-.log`。结尾的 `-` 是滚动标记，
    /// Serilog 会在中间插入日期，写成 `sts-20261011.log`。
    /// </summary>
    public static string ResolveLogSinkPath() =>
        Path.Combine(ResolveLogDirectory(), LogFilePrefix + LogFileExtension);

    /// <summary>
    /// 当天那个日志文件的实际路径（与 <see cref="ResolveLogSinkPath"/> 的滚动规则一致）。
    /// 失败提示框要告诉用户"日志在哪"，所以命名规则只在这里定义一份。
    /// </summary>
    public static string ResolveCurrentLogFile() =>
        Path.Combine(ResolveLogDirectory(), $"{LogFilePrefix}{DateTime.Now:yyyyMMdd}{LogFileExtension}");

    /// <summary>
    /// **数据目录**：程序目录下的 `Data\`（用户 2026-10-14：把原来 `Bins\` 里的两个子目录提到程序目录下、
    /// 去掉 `Bins\` 这一层）—— 库在 `Data\sts.db`、报表与曲线图在 `Report\`。
    /// </summary>
    public static string ResolveDataDirectory() => Path.Combine(AppContext.BaseDirectory, "Data");

    /// <summary>数据库：程序目录下的 `Data\sts.db`（SQLite）。</summary>
    public static string ResolveDatabasePath() =>
        Path.Combine(ResolveDataDirectory(), "sts.db");

    /// <summary>
    /// 报表与曲线图产物根：程序目录下的 `Report\`。**目录结构仍沿用现役**：
    /// `Report\<试验类型>\<单号>\实验报告.<语种>.xlsx` 与 `<试样号>.png`、出厂模板在 `Report\Template\`。
    /// </summary>
    public static string ResolveReportRoot() => Path.Combine(AppContext.BaseDirectory, "Report");

    /// <summary>
    /// **配置文件目录**：程序目录下的 `Config\` —— **所有配置文件都放这里**（用户 2026-10-14 定）。
    /// 首次启动时创建；与产物目录（库在 `Data\`、报表与曲线图在 `Report\`）分开：
    /// 一个是"人可以改的配置"，一个是"程序产出的数据"。
    /// </summary>
    public static string ResolveConfigDirectory() => Path.Combine(AppContext.BaseDirectory, "Config");

    /// <summary>
    /// **系统设置文件**：`Config\settings.json`（用户 2026-10-14：写外部配置文件）。
    ///
    /// 为什么单独放文件、不塞进库（`Data\sts.db`）：
    /// <list type="bullet">
    ///   <item>语言与主题必须在**读库之前**就拿到 —— 连"库架构版本不匹配、请备份后重建"那个提示框
    ///         都得用对的语言显示，用户才看得懂；</item>
    ///   <item>塞进库会牵动架构版本校验：加了表/列就得走迁移，用户现有的库还要面对
    ///         "版本不匹配"的拦截；设置这种"界面偏好"不该有这个代价。</item>
    /// </list>
    /// </summary>
    public static string ResolveSettingsFile() =>
        Path.Combine(ResolveConfigDirectory(), "settings.json");
}
