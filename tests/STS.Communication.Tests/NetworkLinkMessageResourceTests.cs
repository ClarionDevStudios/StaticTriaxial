namespace STS.Communication.Tests;

using System.Text;
using STS.Communication.Channels;
using STS.Communication.NetworkLink;

/// <summary>
/// 网口链路状态的细节文字也要跟语言走（用户 2026-10-15："语言切换功能未处理的内容很多"）：
/// 监视器只带资源键上路，界面按当前语言取译文 —— 这里对着两份语言字典把守。
/// </summary>
public sealed class NetworkLinkMessageResourceTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 链路状态细节的资源键都要有译文(string language)
    {
        var dictionary = File.ReadAllText(FindDictionary(language), Encoding.UTF8);

        var missing = NetworkLinkMessageKeys
            .All.Concat(DeviceSourceMessageKeys.All)
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
