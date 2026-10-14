namespace ANX.Storage.Tests;

using ANX.Core.Settings;

/// <summary>
/// 系统设置的外部配置文件实现（用户 2026-10-14）：键值读写、真落盘、坏文件不炸。
/// </summary>
public sealed class FileAppSettingsServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"anx-settings-{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_directory, "Config", "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task 文件不存在时取值返回空()
    {
        var service = new FileAppSettingsService(SettingsPath);
        Assert.Null(await service.GetAsync(AppSettingKeys.Language));
    }

    [Fact]
    public async Task 写入后能读回_并且真的落到了磁盘()
    {
        var service = new FileAppSettingsService(SettingsPath);

        await service.SetAsync(AppSettingKeys.Language, "en-US");
        await service.SetAsync(AppSettingKeys.Theme, "Light");

        Assert.True(File.Exists(SettingsPath), "设置文件应当被创建");
        Assert.Equal("en-US", await service.GetAsync(AppSettingKeys.Language));
        Assert.Equal("Light", await service.GetAsync(AppSettingKeys.Theme));

        // 换个实例读同一份文件 = 确保不是只写在内存缓存里（"下次启动不生效"就是这个坑）
        var reopened = new FileAppSettingsService(SettingsPath);
        Assert.Equal("en-US", await reopened.GetAsync(AppSettingKeys.Language));
        Assert.Equal("Light", await reopened.GetAsync(AppSettingKeys.Theme));
    }

    [Fact]
    public async Task 覆盖写只改指定键()
    {
        var service = new FileAppSettingsService(SettingsPath);
        await service.SetAsync(AppSettingKeys.Language, "zh-CN");
        await service.SetAsync(AppSettingKeys.Theme, "Dark");

        await service.SetAsync(AppSettingKeys.Language, "en-US");

        Assert.Equal("en-US", await service.GetAsync(AppSettingKeys.Language));
        Assert.Equal("Dark", await service.GetAsync(AppSettingKeys.Theme)); // 没被顺手清掉
    }

    [Fact]
    public async Task 文件损坏时当没有设置_且能重新写入()
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        await File.WriteAllTextAsync(SettingsPath, "{ 这不是合法 JSON ");

        var service = new FileAppSettingsService(SettingsPath);

        // 不抛异常（一个偏好文件坏了不该让软件起不来）
        Assert.Null(await service.GetAsync(AppSettingKeys.Language));

        // 下一次写入把坏文件覆盖掉
        await service.SetAsync(AppSettingKeys.Language, "zh-CN");

        var reopened = new FileAppSettingsService(SettingsPath);
        Assert.Equal("zh-CN", await reopened.GetAsync(AppSettingKeys.Language));
    }

    [Fact]
    public async Task 并发写不同键不丢()
    {
        var service = new FileAppSettingsService(SettingsPath);

        await Task.WhenAll(
            service.SetAsync(AppSettingKeys.Language, "en-US"),
            service.SetAsync(AppSettingKeys.Theme, "Light")
        );

        var reopened = new FileAppSettingsService(SettingsPath);
        Assert.Equal("en-US", await reopened.GetAsync(AppSettingKeys.Language));
        Assert.Equal("Light", await reopened.GetAsync(AppSettingKeys.Theme));
    }

    [Fact]
    public async Task 写盘不留临时文件()
    {
        var service = new FileAppSettingsService(SettingsPath);
        await service.SetAsync(AppSettingKeys.Theme, "Dark");

        var leftovers = Directory.GetFiles(Path.GetDirectoryName(SettingsPath)!, "*.tmp");
        Assert.Empty(leftovers);
    }
}
