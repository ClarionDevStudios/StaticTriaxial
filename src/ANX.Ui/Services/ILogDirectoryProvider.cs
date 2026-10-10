namespace ANX.Ui.Services;

/// <summary>
/// 日志目录的来源。UI 层声明接缝、宿主注入 ——
/// 日志目录由引导层决定（在宿主工程里），UI 层不能反过来引用宿主。
/// </summary>
public interface ILogDirectoryProvider
{
    /// <summary>返回日志目录的绝对路径。</summary>
    string GetLogDirectory();
}
