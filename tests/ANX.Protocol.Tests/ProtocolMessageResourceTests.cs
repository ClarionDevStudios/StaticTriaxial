namespace ANX.Protocol.Tests;

using System.Text;
using ANX.Protocol.Gr3;

/// <summary>
/// 协议层抛给界面的消息（联机/应答超时、设备拒命令）也要跟语言走 ——
/// 协议层只带资源键上路，界面按当前语言取译文；这里对着两份语言字典把守。
/// </summary>
public sealed class ProtocolMessageResourceTests
{
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void 协议消息的资源键都要有译文(string language)
    {
        var dictionary = File.ReadAllText(FindDictionary(language), Encoding.UTF8);

        var missing = ProtocolMessageKeys
            .All.Where(key => !dictionary.Contains($"x:Key=\"{key}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.True(missing.Length == 0, $"{language} 缺这些译文：{string.Join("、", missing)}");
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
