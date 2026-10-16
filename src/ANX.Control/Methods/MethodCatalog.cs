namespace ANX.Control.Methods;

/// <summary>
/// 一个**已装方法**的声明 + 全套服务。宿主按方法目录给，界面/页面只认这些契约、不认具体方法工程
///（"方法工程只在宿主里出现"这条铁律不变）。
///
/// 为什么要有它（用户 2026-10-16 定的第一步：先铺"目录层"）：现在执行契约 / 历史浏览 / 参数 / 报表导出
/// 都是**单例、绑死弯曲**，装第二个方法（马歇尔稳定度）时"同时有两个方法"这件事根本没地方放。
/// </summary>
/// <param name="MethodCode">方法代号（例：T0715）。</param>
/// <param name="MethodName">方法显示名（中文兜底；界面按 <paramref name="MethodNameKey"/> 取当前语言）。</param>
/// <param name="MethodNameKey">方法名的资源键。</param>
/// <param name="Execution">跑试验（执行页）。</param>
/// <param name="Editor">试验单编辑。</param>
/// <param name="Browser">历史数据浏览（查单 / 读明细 / 读曲线 / 读标注）。</param>
/// <param name="Parameters">本方法的参数声明与读写。</param>
/// <param name="Reports">本方法的报表导出（模板选择也留在方法工程里）。</param>
public sealed record TestMethodServices(
    string MethodCode,
    string MethodName,
    string MethodNameKey,
    ITestExecution Execution,
    ITestSheetEditor Editor,
    ITestSheetBrowser Browser,
    ITestParameters Parameters,
    IReportExportService Reports
);

/// <summary>
/// 方法目录：宿主把**已装方法**登记进来。**编译期引用 + 接缝**，不做运行时热插拔
///（与 IAP 的 `IMethodCatalog` 同款口径）。
/// </summary>
public interface IMethodCatalog
{
    /// <summary>已装方法（顺序 = 界面上列出来的顺序）。</summary>
    IReadOnlyList<TestMethodServices> Methods { get; }

    /// <summary>按代号找；没有这个方法回 <c>null</c>。</summary>
    TestMethodServices? Find(string methodCode);
}

/// <summary>目录的朴素实现：一份固定清单。</summary>
public sealed class MethodCatalog(IEnumerable<TestMethodServices> methods) : IMethodCatalog
{
    public IReadOnlyList<TestMethodServices> Methods { get; } = [.. methods];

    public TestMethodServices? Find(string methodCode) =>
        Methods.FirstOrDefault(method =>
            string.Equals(method.MethodCode, methodCode, StringComparison.Ordinal)
        );
}

/// <summary>
/// **当前方法**上下文：现在在用哪一套服务。单方法时就是目录里那一个；
/// 以后加第二个方法，切换只改这里 —— 页面照旧只认契约（`ITestExecution` / `ITestSheetBrowser` …）。
/// </summary>
public interface ICurrentMethodContext
{
    /// <summary>已装方法（首页要按它列"方法入口"卡片）。</summary>
    IReadOnlyList<TestMethodServices> Methods { get; }

    /// <summary>当前方法的那套服务。</summary>
    TestMethodServices Current { get; }

    /// <summary>切换当前方法（代号不存在时抛 <see cref="ArgumentException"/>）。</summary>
    void Select(string methodCode);

    /// <summary>当前方法变了。</summary>
    event EventHandler<TestMethodServices>? Changed;
}

/// <inheritdoc />
public sealed class CurrentMethodContext : ICurrentMethodContext
{
    private readonly IMethodCatalog _catalog;

    public CurrentMethodContext(IMethodCatalog catalog)
    {
        _catalog = catalog;

        Current =
            catalog.Methods.FirstOrDefault()
            ?? throw new InvalidOperationException("方法目录是空的：宿主没有登记任何试验方法");
    }

    /// <inheritdoc />
    public IReadOnlyList<TestMethodServices> Methods => _catalog.Methods;

    /// <inheritdoc />
    public TestMethodServices Current { get; private set; }

    /// <inheritdoc />
    public event EventHandler<TestMethodServices>? Changed;

    /// <inheritdoc />
    public void Select(string methodCode)
    {
        var target =
            _catalog.Find(methodCode)
            ?? throw new ArgumentException($"方法目录里没有 {methodCode}", nameof(methodCode));

        if (ReferenceEquals(target, Current))
        {
            return;
        }

        Current = target;
        Changed?.Invoke(this, target);
    }
}
