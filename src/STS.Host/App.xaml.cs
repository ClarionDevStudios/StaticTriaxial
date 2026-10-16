namespace STS.Host;

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using STS.Ui.Services;
using STS.Ui.ViewModels;
using STS.Ui.Views;
using STS.Ui.Views.Pages;
using STS.Communication.Channels;
using STS.Communication;
using STS.Control;
using STS.Control.Methods;
using STS.Core.Control;
using STS.Core.Methods;
using STS.Core.Settings;
using STS.Method.ShearStrength;
using STS.Protocol.Geo;
using STS.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
// WPF-UI 自带同名接口/类型，用别名消歧（同门工程的做法）。
using IAppThemeService = STS.Ui.Services.IThemeService;
using AppThemeService = STS.Ui.Services.ThemeService;

/// <summary>宿主应用：组装 DI / 日志 / 导航 / 本地化 / 主题，然后交给 ApplicationHostService 开窗。</summary>
public partial class App
{
    /// <summary>同一异常在此时间窗内只问用户一次（防止界面异常反复发生时的弹框风暴）。</summary>
    private static readonly TimeSpan ErrorPromptThrottleWindow = TimeSpan.FromSeconds(30);

    /// <summary>节流表的容量上限（按时间淘汰最旧的，防止签名无限增长）。</summary>
    private const int MaxThrottleSignatures = 16;

    /// <summary>现场串口（全自动土工项目系列；三轴仪为 STM32 主控，协议最高 115200）。先给占位默认，接"通讯设置"页时再挪走。</summary>
    private const string DefaultSerialPortName = "COM1";

    private const int DefaultSerialBaudRate = 115200;

    /// <summary>串口名：可用环境变量 <c>STS_SERIAL_PORT</c> 顶掉（联机调试 / 自检用）。</summary>
    private static string SerialPortName =>
        Environment.GetEnvironmentVariable("STS_SERIAL_PORT") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : DefaultSerialPortName;

    /// <summary>波特率：可用环境变量 <c>STS_SERIAL_BAUD</c> 顶掉。</summary>
    private static int SerialBaudRate =>
        int.TryParse(Environment.GetEnvironmentVariable("STS_SERIAL_BAUD"), out var fromEnvironment)
        && fromEnvironment > 0
            ? fromEnvironment
            : DefaultSerialBaudRate;

    /// <summary>是不是"用模拟器跑"（没有真硬件时的自检开关；模拟器只在 Debug 构建里存在）。</summary>
    private static bool UseSimulator =>
#if SIMULATOR
        string.Equals(
            Environment.GetEnvironmentVariable("STS_USE_SIMULATOR"),
            "1",
            StringComparison.Ordinal
        );
#else
        false;
#endif

    /// <summary>
    /// 现场真机：土工三轴仪（TSZ，串口）。
    /// 帧编解码与设备控制器属"土工协议"层（<c>STS.Protocol.Geo</c>）——当前为**骨架占位**：
    /// 串口通道已就位、真实帧层（FA FB FC + CRC-8，按《全自动土工项目通讯协议 V1.7》）是下一步。
    /// </summary>
    private static ITestDeviceController CreateRealDevice() =>
        new GeoDeviceController(
            new SerialChannel(new SerialChannelOptions(SerialPortName, SerialBaudRate))
        );

    /// <summary>
    /// 模拟器工厂：**只在 Debug 构建里存在**（Release 返回 null → 选了"模拟数据"会回落到真机，
    /// 不会静默假装在出数据）。
    /// </summary>
    private static Func<ITestDeviceController>? CreateSimulator =>
#if SIMULATOR
        () => new SimulatedDeviceController(sampleIntervalSeconds: 0.02, timeScale: 5.0);
#else
        null;
#endif

    /// <summary>
    /// 当前数据来源：环境变量 <c>STS_USE_SIMULATOR=1</c> 优先（自检 / 冒烟）→ 否则读系统设置，
    /// 默认"真机试验"。
    /// </summary>
    private static async Task<string> ResolveDataSourceAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        if (UseSimulator)
        {
            return AppDataSources.Simulator;
        }

        var settings = services.GetService<IAppSettingsService>();
        var value =
            settings is null
                ? null
                : await settings
                    .GetAsync(AppSettingKeys.DataSource, cancellationToken)
                    .ConfigureAwait(false);

        return string.Equals(value, AppDataSources.Simulator, StringComparison.Ordinal)
            ? AppDataSources.Simulator
            : AppDataSources.Device;
    }

    /// <summary>异常签名 → 上次提示时间。签名 = 异常类型 + 消息（不含堆栈），同一处反复出错才算"同一异常"。</summary>
    private static readonly Dictionary<string, DateTime> PromptedErrors = new(StringComparer.Ordinal);

    private static readonly IHost AppHost = BuildHost();

    /// <summary>全局服务定位器（只在宿主层使用；其余工程走构造注入）。</summary>
    public static IServiceProvider Services => AppHost.Services;

    private static IHost BuildHost()
    {
        var logDirectory = AppPaths.ResolveLogDirectory();
        Directory.CreateDirectory(logDirectory);

        // 配置文件目录（用户 2026-10-14：所有配置文件都放程序目录下的 Config\）：
        // 首次启动就建出来，用户一眼能看到"配置放哪儿"。
        Directory.CreateDirectory(AppPaths.ResolveConfigDirectory());

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                AppPaths.ResolveLogSinkPath(),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true
            )
            .CreateLogger();

        // 用全限定名：本文件所在命名空间是 STS.Host，裸写 Host 会与本命名空间末段冲突。
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder();

        // UseSerilog 默认会接管全部日志提供程序（写进 Serilog、移除其它 provider）。
        builder.UseSerilog();

        builder.ConfigureServices(
            (context, services) =>
            {
                _ = services.AddNavigationViewPageProvider();

                // 宿主能力
                _ = services.AddHostedService<ApplicationHostService>();
                _ = services.AddSingleton<ILogDirectoryProvider>(
                    new AppLogDirectoryProvider(logDirectory)
                );

                // 框架服务
                _ = services.AddSingleton<ILocalizationService, LocalizationService>();
                _ = services.AddSingleton<IAppThemeService, AppThemeService>();

                // 系统设置持久化：外部配置文件 Config\settings.json（用户 2026-10-14）
                _ = services.AddSingleton<IAppSettingsService>(
                    new FileAppSettingsService(AppPaths.ResolveSettingsFile())
                );

                // 导航
                _ = services.AddSingleton<INavigationService, NavigationService>();
                _ = services.AddSingleton<INavigationWindow, ShellWindow>();

                // 视图与 ViewModel
                _ = services.AddSingleton<ShellViewModel>();
                // 导航栏开合：外壳持有该状态，页面通过这个服务请求收起（见 INavigationPaneService）
                _ = services.AddSingleton<INavigationPaneService>(sp =>
                    sp.GetRequiredService<ShellViewModel>()
                );
                _ = services.AddSingleton<HomeViewModel>();
                _ = services.AddSingleton<SettingsViewModel>();
                _ = services.AddSingleton<HomePage>();
                _ = services.AddSingleton<SettingsPage>();

                // 数据与试验执行（方法工程只在宿主里出现 —— 铁律）
                _ = services.AddSingleton(new StorageOptions(AppPaths.ResolveDatabasePath()));
                // 抗剪强度（静三轴）三表：**骨架占位列**，周内照现役（E:\Work\土工三轴软件）
                // 与《全自动土工项目通讯协议 V1.7》逐列补齐。建表幂等（CREATE TABLE IF NOT EXISTS）；
                // 软件未发布，改表结构不考虑迁移，开发库对不上版本就备份后重建。
                _ = services.AddSingleton<IMethodPersistenceModule, ShearStrengthPersistenceModule>();
                _ = services.AddSingleton(sp => new DatabaseInitializer(
                    sp.GetRequiredService<StorageOptions>(),
                    sp.GetServices<IMethodPersistenceModule>()
                ));
                _ = services.AddSingleton<SheetNumberGenerator>();
                _ = services.AddSingleton<TestSheetService>();
                _ = services.AddSingleton<ParameterStore>();
                _ = services.AddSingleton<ShearStrengthParameters>();
                // 同一个方法工程实例同时充当"跑试验"和"编辑试验单"两个角色
                _ = services.AddSingleton<ShearStrengthExecution>();
                _ = services.AddSingleton<ShearStrengthSheetBrowser>();
                _ = services.AddSingleton(sp => new ShearStrengthReportExportService(
                    sp.GetRequiredService<TestSheetService>(),
                    AppPaths.ResolveReportRoot(),
                    new LocalizationTextResolver(sp.GetRequiredService<ILocalizationService>()),
                    // 曲线图标注这类"具体类型"依赖照旧取具体类型，别绕 ITestParameters（会成环）。
                    sp.GetRequiredService<ShearStrengthParameters>()
                ));

                // ── 方法目录 + 当前方法（用户 2026-10-16 定的第一步：先铺"目录层"）─────────────
                // 宿主把**已装方法**登记进来（编译期引用 + 接缝，不做运行时热插拔）。
                // 加第二个方法 = 往这份清单里再加一项；页面与 ViewModel 一行都不用改。
                _ = services.AddSingleton<IMethodCatalog>(sp =>
                    new MethodCatalog(
                    [
                        new TestMethodServices(
                            MethodCode: sp.GetRequiredService<ShearStrengthExecution>().MethodCode,
                            MethodName: sp.GetRequiredService<ShearStrengthExecution>().MethodName,
                            MethodNameKey: sp.GetRequiredService<ShearStrengthExecution>().MethodNameKey,
                            Execution: sp.GetRequiredService<ShearStrengthExecution>(),
                            Editor: sp.GetRequiredService<ShearStrengthExecution>(),
                            Browser: sp.GetRequiredService<ShearStrengthSheetBrowser>(),
                            Parameters: sp.GetRequiredService<ShearStrengthParameters>(),
                            Reports: sp.GetRequiredService<ShearStrengthReportExportService>()
                        ),
                    ])
                );
                _ = services.AddSingleton<ICurrentMethodContext, CurrentMethodContext>();

                // 界面那几处契约注入的是**"当前方法"代理**（STS.Control.Methods）：
                // 页面持有的实例永远不变、每次调用落到 CurrentMethodContext.Current 上 ——
                // 于是首页的方法卡片一换方法，执行 / 历史 / 参数 / 报表四条线一起跟着换，
                // 不用重建页面（页面自己要重建的东西——卡片 / 列 / 单号——由它们订 Changed 处理）。
                // 注意别退回"注册时取 Current.X"：那写法在 DI 单例解析的那一刻就把方法定死了。
                _ = services.AddSingleton(sp => new CurrentMethodExecution(
                    () => sp.GetRequiredService<ICurrentMethodContext>()
                ));
                _ = services.AddSingleton<ITestExecution>(sp =>
                    sp.GetRequiredService<CurrentMethodExecution>()
                );
                _ = services.AddSingleton<ITestSheetEditor>(sp =>
                    sp.GetRequiredService<CurrentMethodExecution>()
                );
                _ = services.AddSingleton(sp => new CurrentMethodBrowse(
                    () => sp.GetRequiredService<ICurrentMethodContext>()
                ));
                _ = services.AddSingleton<ITestSheetBrowser>(sp =>
                    sp.GetRequiredService<CurrentMethodBrowse>()
                );
                _ = services.AddSingleton<IReportExportService>(sp =>
                    sp.GetRequiredService<CurrentMethodBrowse>()
                );
                _ = services.AddSingleton<ITestParameters>(sp => new CurrentMethodParameters(
                    () => sp.GetRequiredService<ICurrentMethodContext>()
                ));

                // 网口链路监视（原 ANX 用于 UDP：拔网线不报错，只能从系统层读网卡状态）。
                // **本项目是串口**：网口监视暂不注册（类与测试先留着，待按"串口链路"口径改造后再启用）；
                // SwitchableDeviceController 的 linkMonitor 参数可空，GetService 取到 null 即可。

                // 设备：按**系统设置的"数据来源"**在真机与模拟器之间切换（用户 2026-10-15：
                // 没试件时选"模拟数据"跑通流程）。环境变量 STS_USE_SIMULATOR=1 仍然优先（自检 / 冒烟用）。
                _ = services.AddSingleton(sp => new SwitchableDeviceController(
                    sourceKey: ct => ResolveDataSourceAsync(sp, ct),
                    deviceFactory: CreateRealDevice,
                    simulatorFactory: CreateSimulator,
                    // 链路监视器跟着来源启停（用户 2026-10-16）：真机才盯网线，模拟数据停着
                    linkMonitor: sp.GetService<INetworkLinkMonitor>()
                ));
                _ = services.AddSingleton<ITestDeviceController>(sp =>
                    sp.GetRequiredService<SwitchableDeviceController>()
                );
                _ = services.AddSingleton<IDeviceSourceSwitch>(sp =>
                    sp.GetRequiredService<SwitchableDeviceController>()
                );

                _ = services.AddSingleton(sp => new TestExecutionViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetService<ITestDeviceController>(),
                    sp.GetRequiredService<ICurrentSheetContext>(),
                    sp.GetRequiredService<INavigationService>(),
                    sp.GetRequiredService<IReportExportService>(),
                    sp.GetRequiredService<IModalPageService>(),
                    // 执行页要能先把"编辑哪一件"告诉弹窗的 ViewModel（它是单例，会留着上次的号）
                    sp.GetRequiredService<SampleInfoViewModel>(),
                    // 进执行页要收起导航栏（用户 2026-10-14）
                    sp.GetRequiredService<INavigationPaneService>(),
                    // 网口链路（拔网线要在状态栏看得见）。**服务一直在**（用户 2026-10-16 起由
                    // SwitchableDeviceController 跟着来源启停），界面按"是不是模拟来源"决定要不要报链路
                    sp.GetService<INetworkLinkMonitor>(),
                    // 换试验方法（首页的方法卡片）：本页据此重建指标卡 / 试样列 / 当前单
                    sp.GetRequiredService<ICurrentMethodContext>()
                ));
                _ = services.AddSingleton<TestExecutionPage>();

                // 当前试验单上下文 + 试验单页
                _ = services.AddSingleton<ICurrentSheetContext, CurrentSheetContext>();

                // 把页面当**弹出窗体**打开（用户 2026-10-13：试验参数页、试验单页改弹窗）
                _ = services.AddSingleton<IModalPageService, ModalPageService>();
                _ = services.AddSingleton(sp => new SheetSelectionViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestSheetBrowser>(),
                    sp.GetRequiredService<ITestSheetEditor>(),
                    sp.GetRequiredService<ITestParameters>(),
                    sp.GetRequiredService<ICurrentSheetContext>(),
                    // 换试验方法：试验单字段整套换掉
                    sp.GetRequiredService<ICurrentMethodContext>()
                ));
                _ = services.AddSingleton<SheetSelectionPage>();

                // 历史数据页（查单 / 读明细 / 读曲线 / 导出报表）
                // —— ITestSheetBrowser / IReportExportService 已在上面注册成"当前方法代理"
                // 报表预览渲染：契约在 Core、实现在 Reporting（界面只引用 Core/Control，不认识 FlexCel）
                _ = services.AddSingleton<STS.Core.Reporting.IReportPreviewRenderer,
                    STS.Reporting.FlexCelReportPreviewRenderer>();
                _ = services.AddSingleton(sp => new HistoryViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestSheetBrowser>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetRequiredService<PrintPreviewViewModel>(),
                    sp.GetRequiredService<IModalPageService>()
                ));
                _ = services.AddSingleton<HistoryPage>();

                // 打印预览窗体（用户 2026-10-15：历史页"导出报表"两个按钮合并成这一个入口）
                _ = services.AddSingleton(sp => new PrintPreviewViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<IReportExportService>(),
                    sp.GetRequiredService<STS.Core.Reporting.IReportPreviewRenderer>()
                ));
                _ = services.AddSingleton<PrintPreviewPage>();

                // 试样编辑页 / 参数配置页（同一套参数编辑逻辑，按分组区分）
                // 试样信息填录（逐件）：字段来自 ITestExecution.SampleFields，值存明细行
                _ = services.AddSingleton(sp => new SampleInfoViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetRequiredService<ICurrentSheetContext>(),
                    // 换试验方法：试样信息字段整套换掉
                    sp.GetRequiredService<ICurrentMethodContext>()
                ));
                _ = services.AddSingleton(sp => new MachineSettingsViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestParameters>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetService<ITestDeviceController>(),
                    // 换试验方法：参数声明与值都换一套
                    sp.GetRequiredService<ICurrentMethodContext>()
                ));
                _ = services.AddSingleton<SampleEditorPage>();
                _ = services.AddSingleton<MachineSettingsPage>();
            }
        );

        return builder.Build();
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var logger = Services.GetRequiredService<ILogger<App>>();

        // 注销 / 关机：Windows 要结束本进程，别拦着（拦了会变成"此程序阻止关机"）——
        // 立起退出闸门，主窗口就不会在这条路上弹"确定要退出软件吗？"
        SessionEnding += (_, _) => ExitGuard.AllowExit();

        try
        {
            // 语言与主题从**外部配置文件**读（用户 2026-10-14：改完下次启动要生效）。
            // 注意顺序：这一读必须发生在**建库之前** —— 连"库架构版本不匹配、请备份后重建"
            // 那个提示框都要用用户选的语言显示。
            await ApplyPersistedAppearanceAsync();

            // 建库 / 校验架构版本（版本不匹配会直接抛错，提示备份后重建）
            Services.GetRequiredService<DatabaseInitializer>().Initialize();

            // 网口链路监视：串口项目暂不启用（见注册处的说明）——判空写法保留，改造后即插即用。
            Services.GetService<INetworkLinkMonitor>()?.Start();

            await AppHost.StartAsync();

            logger.LogInformation("启动完成，日志目录：{LogDirectory}", AppPaths.ResolveLogDirectory());

            // 启动即连真机。连不上**只记警告、不拦启动**：现场可能还没上电，
            // 执行页上的「联机」按钮随时可以重试（状态栏会显示未连接）。
            try
            {
                await Services
                    .GetRequiredService<TestExecutionViewModel>()
                    .ConnectCommand.ExecuteAsync(null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "启动时联机失败，可在执行页点「联机」重试");
            }

        }
        catch (SchemaVersionMismatchException ex)
        {
            // 可预期的失败：把"哪个库、差多少、下一步做什么"讲清楚，然后**正常退出**。
            logger.LogError(ex, "启动失败：数据库架构版本不匹配");
            ShowStartupFailureDialog(Text("Startup.SchemaMismatch.Title"), BuildSchemaMismatchText(ex));
            Shutdown(1);
            return;
        }
        catch (Exception ex)
        {
            // 其它启动失败（DI / 主题 / 启动服务等任一环节）：给一句人话 + 日志文件路径，
            // 堆栈留在日志里。同样不向上抛 —— 否则 WPF 会再盖一个原始错误框。
            logger.LogError(ex, "启动失败");
            ShowStartupFailureDialog(Text("Startup.Failed.Title"), BuildUnexpectedFailureText(ex));
            Shutdown(1);
            return;
        }
    }

    /// <summary>配置文件里没有（或值不认得）时用的默认外观。</summary>
    private const string DefaultLanguage = "zh-CN";

    private const string DefaultTheme = "Dark";

    /// <summary>
    /// 按**外部配置文件**应用语言与主题（用户 2026-10-14：改完下次启动要生效）。
    ///
    /// 容错口径：配置文件缺失、键缺失、值不认得（比如手工改错了）→ 一律**回默认值继续启动**；
    /// 一个界面偏好不该让软件起不来。语言要按受支持列表校验（语言服务对不认得的代码是
    /// "静默不切换"，不校验就会停在空语言上）；主题服务本身对不认得的名字会归一到 Dark。
    /// </summary>
    private static async Task ApplyPersistedAppearanceAsync()
    {
        var logger = Services.GetRequiredService<ILogger<App>>();
        var settings = Services.GetRequiredService<IAppSettingsService>();
        var localization = Services.GetRequiredService<ILocalizationService>();

        var language = await settings.GetAsync(AppSettingKeys.Language).ConfigureAwait(true)
            ?? DefaultLanguage;

        if (!localization.Languages.Any(entry => entry.Code == language))
        {
            logger.LogWarning("配置文件里的语言 {Language} 不受支持，回落 {Default}", language, DefaultLanguage);
            language = DefaultLanguage;
        }

        localization.SetLanguage(language);

        var theme = await settings.GetAsync(AppSettingKeys.Theme).ConfigureAwait(true)
            ?? DefaultTheme;

        Services.GetRequiredService<IAppThemeService>().Apply(theme);

        logger.LogInformation("按配置文件应用外观：语言 {Language}，主题 {Theme}", language, theme);
    }

    /// <summary>库版本对不上：库版本 / 程序要求 / 要备份的文件，逐条给出。</summary>
    private static string BuildSchemaMismatchText(SchemaVersionMismatchException mismatch) =>
        string.Join(
            // 换行在代码里拼：XAML/BAML 会把资源文本里的 CR/LF 归一化掉，写进资源留不住
            Environment.NewLine,
            Text("Startup.SchemaMismatch.Summary"),
            string.Empty,
            string.Format(
                Text("Startup.SchemaMismatch.Versions"),
                mismatch.DatabaseVersion,
                mismatch.RequiredVersion
            ),
            string.Empty,
            Text("Startup.SchemaMismatch.Hint"),
            mismatch.DatabasePath
        );

    /// <summary>其它启动失败：错误信息 + 日志文件路径（细节在日志里，这里不铺堆栈）。</summary>
    private static string BuildUnexpectedFailureText(Exception ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().FullName! : ex.Message;

        return string.Join(
            Environment.NewLine,
            Text("Startup.Failed.Summary"),
            string.Empty,
            string.Format(Text("Startup.Failed.Detail"), detail),
            string.Empty,
            Text("Startup.Failed.LogHint"),
            AppPaths.ResolveCurrentLogFile()
        );
    }

    /// <summary>
    /// 启动失败的统一提示框。此刻外壳还没建起来（没有宿主窗口、也没有内容对话框宿主），
    /// 所以用系统 MessageBox —— 它必须在任何窗口之前就能弹出来。
    /// </summary>
    private static void ShowStartupFailureDialog(string title, string text) =>
        MessageBox.Show(text, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>
    /// 取资源文案；本地化服务本身没起来时退回键名 —— 提示框不能再抛第二次异常。
    /// 真走到这一步时，提示框里的"日志文件路径"仍会照常显示，用户依然有线索。
    /// </summary>
    private static string Text(string key)
    {
        try
        {
            var value = Services.GetRequiredService<ILocalizationService>().GetString(key);
            return string.IsNullOrWhiteSpace(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        // 分阶段记日志：退出路径一旦慢（曾见失败路径耗时 60s），先看是哪一段
        Log.Information("退出：开始收尾，退出码 {ExitCode}", e.ApplicationExitCode);

        Log.Information("退出：停止宿主…");
        await AppHost.StopAsync();

        Log.Information("退出：释放宿主…");
        AppHost.Dispose();

        Log.Information("退出：刷新日志…");
        await Log.CloseAndFlushAsync();
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e
    )
    {
        Log.Error(e.Exception, "界面线程未处理异常");

        // 两种选择都算"已处理"，否则 WPF 在处理器返回后会按未处理异常终止进程。
        e.Handled = true;

        // 同一异常短时间内反复发生：不再打扰用户，按"继续运行"处理（日志里仍然每次都有 ERR）
        var signature = BuildErrorSignature(e.Exception);
        var now = DateTime.UtcNow;

        if (IsThrottled(signature, now))
        {
            Log.Warning(
                "同一异常在 {Window} 秒内重复出现，只写日志、不再提示",
                (int)ErrorPromptThrottleWindow.TotalSeconds
            );
            return;
        }

        RememberPrompt(signature, now);

        var shouldExit = true;

        try
        {
            shouldExit = AskWhetherToExitOnUiError(e.Exception);
        }
        catch (Exception dialogFailure)
        {
            // 连提示框都弹不出来（界面恐怕已经坏了）：记下来，按"退出"处理
            Log.Error(dialogFailure, "界面异常提示框弹出失败，按退出处理");
        }

        if (shouldExit)
        {
            Log.Information("界面线程未处理异常：用户选择退出");
            // 已经问过一遍了（那个框里就是"是否退出程序？"）：立闸门，别让主窗口再问一次
            ExitGuard.AllowExit();
            Shutdown(1);
            return;
        }

        Log.Information("界面线程未处理异常：用户选择继续运行");
    }

    /// <summary>异常签名：类型 + 消息（截断），不含堆栈 —— 同一处反复出错才算"同一异常"。</summary>
    private static string BuildErrorSignature(Exception ex)
    {
        const int maxMessageLength = 300;
        var message = ex.Message ?? string.Empty;

        return message.Length > maxMessageLength
            ? $"{ex.GetType().FullName}: {message[..maxMessageLength]}"
            : $"{ex.GetType().FullName}: {message}";
    }

    private static bool IsThrottled(string signature, DateTime now)
    {
        lock (PromptedErrors)
        {
            return PromptedErrors.TryGetValue(signature, out var last)
                && now - last < ErrorPromptThrottleWindow;
        }
    }

    /// <summary>记下"刚提示过"，顺带剪掉过期项、按时间淘汰最旧的，控制表的大小。</summary>
    private static void RememberPrompt(string signature, DateTime now)
    {
        lock (PromptedErrors)
        {
            PromptedErrors[signature] = now;

            foreach (
                var stale in PromptedErrors
                    .Where(entry => now - entry.Value >= ErrorPromptThrottleWindow)
                    .Select(entry => entry.Key)
                    .ToArray()
            )
            {
                _ = PromptedErrors.Remove(stale);
            }

            while (PromptedErrors.Count > MaxThrottleSignatures)
            {
                var oldest = PromptedErrors.OrderBy(entry => entry.Value).First().Key;
                _ = PromptedErrors.Remove(oldest);
            }
        }
    }

    /// <summary>
    /// 问用户：带着这次异常继续跑，还是退出。返回 <c>true</c> 表示退出。
    /// 「是 / 否」的按钮文案由系统提供，所以选项含义写进正文里，不让人去猜。
    /// </summary>
    private static bool AskWhetherToExitOnUiError(Exception ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Message)
            ? ex.GetType().Name
            : $"{ex.GetType().Name}: {ex.Message}";

        var text = string.Join(
            Environment.NewLine,
            Text("Runtime.Error.Summary"),
            string.Empty,
            string.Format(Text("Runtime.Error.Detail"), detail),
            string.Empty,
            Text("Runtime.Error.LogHint"),
            AppPaths.ResolveCurrentLogFile(),
            string.Empty,
            string.Format(
                Text("Runtime.Error.ThrottleNote"),
                (int)ErrorPromptThrottleWindow.TotalSeconds
            ),
            string.Empty,
            Text("Runtime.Error.Question")
        );

        // 有主窗口就当它的模态对话框（浮在前面）；没有（或已关闭）就别传 owner，传了会抛
        var owner = Application.Current?.MainWindow;
        if (owner is { IsLoaded: false })
        {
            owner = null;
        }

        var result =
            owner is null
                ? MessageBox.Show(
                    text,
                    Text("Runtime.Error.Title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error
                )
                : MessageBox.Show(
                    owner,
                    text,
                    Text("Runtime.Error.Title"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error
                );

        return result == MessageBoxResult.Yes;
    }
}
