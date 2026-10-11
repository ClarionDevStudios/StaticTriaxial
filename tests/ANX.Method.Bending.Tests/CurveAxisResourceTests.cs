namespace ANX.Method.Bending.Tests;

using System.Text;

/// <summary>
/// 方法声明的文案走"方法给资源键、界面 / 报表按键解析"这条路（IAP 同款）：
/// 键一旦不在语言字典里，界面上会显示成 `⚠键名`。这里守住这条一致性。
/// </summary>
public sealed class CurveAxisResourceTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 方法声明的资源键应存在于语言字典(string language)
    {
        var dictionary = File.ReadAllText(FindDictionary(language), Encoding.UTF8);

        foreach (var key in BendingMetricKeys.All)
        {
            Assert.Contains($"x:Key=\"{key}\"", dictionary, StringComparison.Ordinal);
        }
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
