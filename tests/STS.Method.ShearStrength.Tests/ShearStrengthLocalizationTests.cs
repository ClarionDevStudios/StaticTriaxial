namespace STS.Method.ShearStrength.Tests;

using System.Text;

/// <summary>
/// 声明里给了键就必须在两种语言里都有译文（照"链路状态细节"同款做法）：
/// 试样信息字段（显示名 + 说明）与方法消息键，都对着两份语言字典把守。
/// </summary>
public sealed class ShearStrengthLocalizationTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 试样信息字段与方法消息的资源键都要有译文(string language)
    {
        var dictionary = File.ReadAllText(FindDictionary(language), Encoding.UTF8);

        var keys = ShearStrengthMessageKeys
            .All.Concat(
                ShearStrengthMetricKeys.All
            )
            .Concat(
                ShearStrengthSampleFields.Definitions.SelectMany(definition =>
                    new[] { definition.DisplayNameKey, definition.DescriptionKey }.Where(key =>
                        !string.IsNullOrEmpty(key)
                    )
                )
            )
            .Concat(
                ShearStrengthSheetFields.Definitions.SelectMany(definition =>
                    new[] { definition.DisplayNameKey, definition.DescriptionKey }.Where(key =>
                        !string.IsNullOrEmpty(key)
                    )
                )
            )
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var missing = keys
            .Where(key => !dictionary.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.True(missing.Length == 0, $"{language} 缺这些译文：{string.Join("、", missing)}");
    }

    /// <summary>从测试输出目录往上找仓库根（认 STS.slnx），再定位语言字典。</summary>
    private static string FindDictionary(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "STS.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "没找到仓库根（STS.slnx）；测试依赖仓库目录结构");

        var path = Path.Combine(
            directory!.FullName,
            "src",
            "STS.Ui",
            "Resources",
            "Languages",
            $"{language}.xaml"
        );

        Assert.True(File.Exists(path), $"语言字典不存在：{path}");
        return path;
    }
}
