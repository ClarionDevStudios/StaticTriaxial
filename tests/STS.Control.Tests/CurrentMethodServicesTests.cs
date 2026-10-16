namespace STS.Control.Tests;

using STS.Control;
using STS.Control.Methods;
using STS.Core;
using STS.Core.Control;
using STS.Core.TestSheets;

/// <summary>
/// **"当前方法"代理**：页面持有的执行 / 编辑 / 浏览 / 报表 / 参数五件套永远是同一个对象，
/// 每次调用落到 <see cref="ICurrentMethodContext.Current"/> 上 —— 换方法不用重建页面。
/// 这条机制是"首页方法卡片 + 页面按当前方法取数"的地基，所以单独钉住。
/// </summary>
public sealed class CurrentMethodServicesTests
{
    [Fact]
    public void 代理应跟着当前方法切换()
    {
        var context = new CurrentMethodContext(
            new MethodCatalog([Method("TST1"), Method("TST2")])
        );

        var execution = new CurrentMethodExecution(() => context);
        var browse = new CurrentMethodBrowse(() => context);
        var parameters = new CurrentMethodParameters(() => context);

        Assert.Equal("TST1", execution.MethodCode);
        Assert.Equal("TST1", execution.SheetInfoFields[0].Key); // 编辑契约也走同一个代理
        Assert.Equal("TST1", browse.CurveXAxisKey);
        Assert.Equal("TST1", parameters.Definitions[0].Key);

        context.Select("TST2");

        Assert.Equal("TST2", execution.MethodCode);
        Assert.Equal("TST2", execution.SheetInfoFields[0].Key);
        Assert.Equal("TST2", browse.CurveXAxisKey);
        Assert.Equal("TST2", parameters.Definitions[0].Key);
    }

    private static TestMethodServices Method(string code) =>
        new(
            code,
            $"{code} 方法",
            $"Method.{code}.Name",
            Execution: new FakeExecution(code),
            Editor: new FakeExecution(code),
            Browser: new FakeBrowser(code),
            Parameters: new FakeParameters(code),
            Reports: new FakeBrowser(code)
        );

    /// <summary>极简假实现：只让被断言的成员按方法代号回值，其余一律抛（被误用立刻暴露）。</summary>
    private sealed class FakeExecution(string code) : ITestExecution, ITestSheetEditor
    {
        public string MethodCode => code;

        public string MethodName => $"{code} 方法";

        public string MethodNameKey => $"Method.{code}.Name";

        public string StandardReference => $"JTG {code}";

        public string CurveXAxisKey => code;

        public string CurveYAxisKey => code;

        public TestRunProfile Profile => new();

        public IReadOnlyList<MetricDefinition> Metrics => [];

        public IReadOnlyList<ParameterDefinition> SampleFields => [];

        public IReadOnlyList<ParameterDefinition> SheetInfoFields => [new(code, code, "", "0")];

        public Task<TestRunProfile> LoadProfileAsync(
            TestStartContext? context = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Profile);

        Task<IReadOnlyDictionary<string, string>> ITestExecution.LoadSampleInfoAsync(
            string sheetId,
            string sampleId,
            CancellationToken cancellationToken
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SampleRowInfo>> ReadSampleRowsAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SaveSampleInfoAsync(
            string sheetId,
            string sampleId,
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string> CreateSheetAsync(
            string projectName,
            string numberPrefix,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string> NextSampleIdAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<bool> DeleteSampleAsync(
            string sheetId,
            string sampleId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, double>?> CompleteSampleAsync(
            string sheetId,
            string sampleId,
            IReadOnlyList<CurvePoint> curve,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task CompleteSheetAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<ValidityReport> EvaluateValidityAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<RecomputeReport> RecomputeSheetAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string> PeekNextSheetIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult($"{code}-001");

        public Task<IReadOnlyDictionary<string, string>> ReadSheetInfoAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string> CreateSheetAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task UpdateSheetAsync(
            string sheetId,
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task PushTestConditionsAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public IReadOnlyDictionary<string, string> ResolveDependentDefaults(
            string changedKey,
            string value
        ) => new Dictionary<string, string>();

        public Task<bool> HasTemplateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, string>> ReadTemplateAsync(
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SaveTemplateAsync(
            IReadOnlyDictionary<string, string> fields,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeBrowser(string code) : ITestSheetBrowser, IReportExportService
    {
        public string CurveXAxisKey => code;

        public string CurveYAxisKey => code;

        public Task<IReadOnlyList<TestSheetSummary>> QueryAsync(
            string? keyword = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<TestSheetSummary>>([]);

        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ReadSamplesAsync(
            string sheetId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<CurvePoint>> ReadCurveAsync(
            string sheetId,
            string sampleId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<CurveAnnotation?> ReadAnnotationAsync(
            string sheetId,
            string sampleId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string> ExportAsync(
            string sheetId,
            string culture,
            CancellationToken cancellationToken = default
        ) => Task.FromResult($"{code}-{sheetId}-{culture}");

        public Task<string?> SaveCurveImageAsync(
            string sheetId,
            string sampleId,
            string culture,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);
    }

    private sealed class FakeParameters(string code) : ITestParameters
    {
        public IReadOnlyList<ParameterDefinition> Definitions => [new(code, code, "", "0")];

        public Task<IReadOnlyDictionary<string, string>> LoadAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>()
        );

        public Task SaveAsync(
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
