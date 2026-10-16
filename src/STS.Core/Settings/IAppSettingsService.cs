namespace STS.Core.Settings;

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

    /// <summary>
    /// **数据来源**（<see cref="AppDataSources.Device"/> / <see cref="AppDataSources.Simulator"/>）。
    ///
    /// 用户 2026-10-15：没有试件时用模拟器跑通流程，就在系统设置里切这一项，
    /// 不必再靠环境变量 <c>STS_USE_SIMULATOR</c>（那个保留作自检 / 冒烟用）。
    /// </summary>
    public const string DataSource = "system.dataSource";
}

/// <summary>数据来源取值。</summary>
public static class AppDataSources
{
    /// <summary>真机试验（默认）：与设备联机，用真实试件做试验。</summary>
    public const string Device = "device";

    /// <summary>模拟数据：启动试验后由模拟器产生试验数据（用于缺试件时验证流程）。</summary>
    public const string Simulator = "simulator";
}
