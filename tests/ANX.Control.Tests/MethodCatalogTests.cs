namespace ANX.Control.Tests;

using ANX.Control;
using ANX.Control.Methods;

/// <summary>
/// 方法目录 + 当前方法上下文（用户 2026-10-16 定的第一步：先铺"目录层"）。
///
/// 这里要证明的那件事：**第二个方法能被登记、能被选中，并且取到的是它自己那条记录** ——
/// 也就是"框架装得下第二种方法"。服务本身（执行 / 浏览 / 参数 / 报表）由宿主真实装配，
/// 目录只负责"登记与挑选"，所以本用例不去调它们（传占位）。
/// </summary>
public sealed class MethodCatalogTests
{
    [Fact]
    public void 先登记的方法默认是当前方法()
    {
        var context = new CurrentMethodContext(
            new MethodCatalog([Services("T0715"), Services("T0709")])
        );

        Assert.Equal("T0715", context.Current.MethodCode);
        Assert.Equal(2, context.Methods.Count);
    }

    [Fact]
    public void 选中第二个方法_取到它自己那条记录_并发出通知()
    {
        var context = new CurrentMethodContext(
            new MethodCatalog([Services("T0715"), Services("T0709")])
        );

        var changed = new List<string>();
        context.Changed += (_, method) => changed.Add(method.MethodCode);

        context.Select("T0709");

        Assert.Equal("T0709", context.Current.MethodCode);
        Assert.Equal("Method.T0709.Name", context.Current.MethodNameKey);
        Assert.Equal(["T0709"], changed);

        // 选同一个方法不发通知（幂等）
        context.Select("T0709");
        Assert.Single(changed);
    }

    [Fact]
    public void 空目录直接报错_选不存在的代号也报错()
    {
        _ = Assert.Throws<InvalidOperationException>(() =>
            new CurrentMethodContext(new MethodCatalog([]))
        );

        var context = new CurrentMethodContext(new MethodCatalog([Services("T0715")]));
        _ = Assert.Throws<ArgumentException>(() => context.Select("T9999"));
    }

    [Fact]
    public void 按代号查得到_查不到回null()
    {
        var catalog = new MethodCatalog([Services("T0715"), Services("T0709")]);

        Assert.Equal("T0709", catalog.Find("T0709")?.MethodCode);
        Assert.Null(catalog.Find("T9999"));
    }

    /// <summary>只带身份的假记录（目录不碰服务本身，所以四个契约用占位）。</summary>
    private static TestMethodServices Services(string methodCode) =>
        new(
            MethodCode: methodCode,
            MethodName: methodCode,
            MethodNameKey: $"Method.{methodCode}.Name",
            Execution: null!,
            Editor: null!,
            Browser: null!,
            Parameters: null!,
            Reports: null!
        );
}
