namespace ANX.Storage;

using System.Text.Json;
using ANX.Core.Settings;

/// <summary>
/// 系统设置的**外部配置文件**实现（用户 2026-10-14 定："写外部配置文件"）。
///
/// 形态：程序目录 `Config\settings.json`，内容就是一个键值表
/// <c>{ "system.language": "zh-CN", "system.theme": "Dark" }</c> —— 与
/// <see cref="IAppSettingsService"/> 的键值语义一一对应，人也能看懂、能手工改。
///
/// 几个刻意的取舍：
/// <list type="bullet">
///   <item>**整份读进内存**：设置项就几条，没必要每次读盘；写入时整体重写。</item>
///   <item>**写盘是"临时文件 + 原子替换"**：中途掉电/被杀不会留下半截 JSON，
///         否则下次启动解析失败、用户会莫名回到默认设置。</item>
///   <item>**读坏了不抛异常**：文件缺失 / JSON 损坏一律当"没有设置"（回默认值），
///         应用程序不该因为一个偏好文件起不来；下一次写入自然覆盖掉坏文件。</item>
///   <item>写失败**照实抛出**：那是真问题（目录只读、磁盘满），由调用方决定怎么报。</item>
/// </list>
/// </summary>
public sealed class FileAppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;

    /// <summary>串行化"读-改-写"，避免两个调用同时改同一份设置互相覆盖。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Dictionary<string, string>? _values;

    public FileAppSettingsService(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var values = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return values.TryGetValue(key, out var value) ? value : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var values = await LoadAsync(cancellationToken).ConfigureAwait(false);
            values[key] = value;
            await SaveAsync(values, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取整份设置（缓存命中就不读盘）。</summary>
    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_values is not null)
        {
            return _values;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            if (File.Exists(_path))
            {
                var json = await File
                    .ReadAllTextAsync(_path, cancellationToken)
                    .ConfigureAwait(false);

                if (
                    JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions)
                    is { } parsed
                )
                {
                    // 用序数比较重建一份：文件里可能存过大小写不同的同名键
                    values = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 读不到 / 读坏了：当"没有设置"，回默认值（下一次写入会覆盖）
            values.Clear();
        }

        _values = values;
        return values;
    }

    /// <summary>整份写回：先写临时文件，再原子替换（避免半截文件）。</summary>
    private async Task SaveAsync(
        Dictionary<string, string> values,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(values, SerializerOptions);
        var temporary = _path + ".tmp";

        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, _path, overwrite: true);
    }
}
