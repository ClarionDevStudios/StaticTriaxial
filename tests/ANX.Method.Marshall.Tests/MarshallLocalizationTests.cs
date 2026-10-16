namespace ANX.Method.Marshall.Tests;

using System.Text;
using ANX.Control;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// T 0709 的**多语言**（用户 2026-10-15："语言切换功能未处理的内容很多，我们挨个处理"）：
/// 声明里挂的资源键（参数 / 试样字段 / 试验单字段 / 指标 / 消息）**必须在两份语言字典里都有译文**，
/// 漏翻立刻红，而不是在界面上显示成 ⚠ 键名。
/// </summary>
public sealed class MarshallLocalizationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"anx-t0709-i18n-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 参数试样字段与试验单字段的资源键都要有译文(string language)
    {
        var dictionary = File.ReadAllText(FindDictionary(language), Encoding.UTF8);
        var missing = new List<string>();

        foreach (var definition in DefinitionsUnderTest())
        {
            Check(definition.DisplayNameKey, $"{definition.Key} 显示名", required: true);
            Check(
                definition.DescriptionKey,
                $"{definition.Key} 说明",
                required: !string.IsNullOrWhiteSpace(definition.Description)
            );

            foreach (var option in definition.Options ?? [])
            {
                Check(option.LabelKey, $"{definition.Key} 选项 {option.Value}", required: true);
            }
        }

        Assert.True(missing.Count == 0, $"{language} 缺这些译文：{string.Join("、", missing)}");
        return;

        void Check(string key, string what, bool required)
        {
            if (string.IsNullOrEmpty(key))
            {
                if (required)
                {
                    missing.Add($"{what}（还没接资源键）");
                }

                return;
            }

            if (!dictionary.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal))
            {
                missing.Add($"{what} → {key}");
            }
        }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 指标与消息的资源键都要有译文(string language)
    {
        var dictionary = File.ReadAllText(FindDictionary(language), Encoding.UTF8);

        var missing = MarshallMetricKeys
            .All.Concat(MarshallMessageKeys.All)
            .Where(key => !dictionary.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.True(missing.Length == 0, $"{language} 缺这些译文：{string.Join("、", missing)}");
    }

    private IEnumerable<ParameterDefinition> DefinitionsUnderTest()
    {
        var module = new MarshallPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_root, "Data", "anx.db")),
            [module]
        );
        initializer.Initialize();

        var method = new MarshallParameters(new ParameterStore(initializer));

        return
        [
            .. MachineParameterCatalog.Definitions,
            .. MarshallSampleFields.Definitions,
            .. MarshallSheetFields.Definitions,
            .. method.Definitions,
        ];
    }

    /// <summary>从测试输出目录往上找仓库根（认 ANX.slnx），再定位语言字典。</summary>
    private static string FindDictionary(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ANX.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "没找到仓库根（ANX.slnx）；测试依赖仓库目录结构");

        var path = Path.Combine(
            directory!.FullName,
            "src",
            "ANX.Ui",
            "Resources",
            "Languages",
            $"{language}.xaml"
        );

        Assert.True(File.Exists(path), $"语言字典不存在：{path}");
        return path;
    }
}
