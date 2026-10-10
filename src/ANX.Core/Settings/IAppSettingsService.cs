namespace ANX.Core.Settings;

/// <summary>
/// 系统设置持久化。设置天然是键值对，用键值表承载即可，不需要为每项建强类型列。
/// 引导层仍然用文件（数据库路径、日志目录这类"读库之前就要知道"的信息）。
/// </summary>
public interface IAppSettingsService
{
    /// <summary>取值；不存在返回 null。</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>写值（不存在则新增）。</summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken = default);
}

/// <summary>已知设置键。键名一经发布视为对外约定，不改名。</summary>
public static class AppSettingKeys
{
    /// <summary>界面语言（例 zh-CN / en-US）。</summary>
    public const string Language = "system.language";

    /// <summary>主题（Dark / Light）。</summary>
    public const string Theme = "system.theme";
}
