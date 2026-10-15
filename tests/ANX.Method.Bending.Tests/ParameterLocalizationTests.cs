namespace ANX.Method.Bending.Tests;

using System.Text;
using ANX.Control;
using ANX.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 参数 / 试样信息字段的**多语言**（用户 2026-10-15："语言切换功能未处理的内容很多，我们挨个处理"）：
/// 声明里按 <see cref="ParameterTextKeys"/> 的约定挂上资源键，界面按当前语言取。
/// 这里对着两份语言字典把守 —— **声明了键就必须有译文**，漏翻立刻红，而不是在界面上显示 ⚠ 键名。
/// </summary>
public sealed class ParameterLocalizationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"anx-i18n-{Guid.NewGuid():N}");

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
    public void 参数与试样字段的显示文本资源键都要有译文(string language)
    {
        var dictionary = File.ReadAllText(FindDictionary(language), Encoding.UTF8);

        var missing = new List<string>();

        // 方法层抛给界面的消息（BendingMessageKeys）+ 界面自己用到的通用键
        foreach (var key in BendingMessageKeys.All.Concat(["Common.ListSeparator"]))
        {
            if (!dictionary.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal))
            {
                missing.Add($"消息 {key}");
            }
        }

        foreach (var definition in DefinitionsUnderTest())
        {
            Check(definition.DisplayNameKey, $"{definition.Key} 显示名", required: true);
            // 说明是**可选**的：声明里本来没有说明的项（试样信息字段）就不该挂键
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

    /// <summary>
    /// 待整改的声明集。**逐块迁**：一块接完就加进来（现在 = 机器参数 + 方法参数 + 试样信息字段），
    /// 试验单字段（<c>BendingSheetFields</c>）等下一块。
    /// </summary>
    private IEnumerable<ParameterDefinition> DefinitionsUnderTest()
    {
        var module = new BendingPersistenceModule();
        var initializer = new DatabaseInitializer(
            new StorageOptions(Path.Combine(_root, "Data", "anx.db")),
            [module]
        );
        initializer.Initialize();

        var method = new BendingParameters(new ParameterStore(initializer));

        return [
            .. MachineParameterCatalog.Definitions,
            .. BendingSampleFields.Definitions,
            .. BendingSheetFields.Definitions,
            .. method.Definitions,
        ];
    }

    /// <summary>从测试输出目录往上找仓库根（认 ANX.slnx），再定位语言字典（与 CurveAxisResourceTests 同款）。</summary>
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
