namespace ANX.Host;

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using ANX.Ui.Views;
using ANX.Ui.Views.Pages;
using ANX.Communication.Channels;
using ANX.Communication;
using ANX.Communication.NetworkLink;
using ANX.Control;
using ANX.Core.Control;
using ANX.Core.Methods;
using ANX.Core.Settings;
using ANX.Method.Bending;
using ANX.Protocol.Gr3;
using ANX.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Wpf.Ui;
using Wpf.Ui.DependencyInjection;
// WPF-UI 自带同名接口/类型，用别名消歧（同门工程的做法）。
using IAppThemeService = ANX.Ui.Services.IThemeService;
using AppThemeService = ANX.Ui.Services.ThemeService;

/// <summary>宿主应用：组装 DI / 日志 / 导航 / 本地化 / 主题，然后交给 ApplicationHostService 开窗。</summary>
public partial class App
{
    /// <summary>同一异常在此时间窗内只问用户一次（防止界面异常反复发生时的弹框风暴）。</summary>
    private static readonly TimeSpan ErrorPromptThrottleWindow = TimeSpan.FromSeconds(30);

    /// <summary>节流表的容量上限（按时间淘汰最旧的，防止签名无限增长）。</summary>
    private const int MaxThrottleSignatures = 16;

    /// <summary>现场 GR-3 控制器的 UDP 地址（现役同款默认值；将来挪进设置页）。</summary>
    private const string DefaultDeviceHost = "192.168.0.122";

    /// <summary>控制器地址：默认用现场值，可用环境变量 <c>ANX_DEVICE_HOST</c> 顶掉（联机调试 / 自检用）。</summary>
    private static string DeviceHost =>
        Environment.GetEnvironmentVariable("ANX_DEVICE_HOST") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : DefaultDeviceHost;

    /// <summary>是不是"用模拟器跑"（没有真硬件时的自检开关；模拟器只在 Debug 构建里存在）。</summary>
    private static bool UseSimulator =>
#if SIMULATOR
        string.Equals(
            Environment.GetEnvironmentVariable("ANX_USE_SIMULATOR"),
            "1",
            StringComparison.Ordinal
        );
#else
        false;
#endif

    private const int DevicePort = 5001;

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

        // 用全限定名：本文件所在命名空间是 ANX.Host，裸写 Host 会与本命名空间末段冲突。
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
                _ = services.AddSingleton<IMethodPersistenceModule, BendingPersistenceModule>();
                _ = services.AddSingleton(sp => new DatabaseInitializer(
                    sp.GetRequiredService<StorageOptions>(),
                    sp.GetServices<IMethodPersistenceModule>()
                ));
                _ = services.AddSingleton<SheetNumberGenerator>();
                _ = services.AddSingleton<TestSheetService>();
                _ = services.AddSingleton<ParameterStore>();
                _ = services.AddSingleton<BendingParameters>();
                _ = services.AddSingleton<ITestParameters>(sp => sp.GetRequiredService<BendingParameters>());
                // 同一个方法工程实例同时充当"跑试验"和"编辑试验单"两个角色
                _ = services.AddSingleton<BendingExecution>();
                _ = services.AddSingleton<ITestExecution>(sp =>
                    sp.GetRequiredService<BendingExecution>()
                );
                _ = services.AddSingleton<ITestSheetEditor>(sp =>
                    sp.GetRequiredService<BendingExecution>()
                );

                // 网口链路监视（用户 2026-10-14）：UDP 无连接，拔网线不报错，只能从系统层读网卡链路状态。
                // 设备地址可被环境变量 ANX_DEVICE_HOST 顶掉 —— 联机调试 / 自检时指到别处用。
                // **模拟器模式不注册**：那时根本没有真设备，报"真网线断了"只会是噪音（VM 取到 null 就不管链路）。
                if (!UseSimulator)
                {
                    _ = services.AddSingleton<INetworkLinkMonitor>(_ =>
                    {
                        var monitor = new NetworkLinkMonitor(DeviceHost);

                        // 链路变化落日志（网线插拔这类现场问题，事后要能查）
                        monitor.StateChanged += (_, state) =>
                            Log.Information(
                                "网口链路 {Kind}：{Detail}（网卡：{Adapter}）",
                                state.Kind,
                                state.Detail,
                                state.AdapterName
                            );

                        return monitor;
                    });
                }

                // 设备：默认连**现场真机**（GR-3 控制器走 UDP，地址沿用现役）。
                // 想在没有硬件时试跑，设环境变量 ANX_USE_SIMULATOR=1（模拟器只在 Debug 构建里存在）。
                _ = services.AddSingleton<ITestDeviceController>(_ =>
                {
#if SIMULATOR
                    if (
                        string.Equals(
                            Environment.GetEnvironmentVariable("ANX_USE_SIMULATOR"),
                            "1",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return new SimulatedDeviceController(
                            sampleIntervalSeconds: 0.02,
                            timeScale: 5.0
                        );
                    }
#endif
                    return new Gr3DeviceController(
                        new UdpChannel(new UdpChannelOptions(DeviceHost, DevicePort, DevicePort)),
                        // 轴向压力 = 压力通道 1（索引 0，100.000kN 主传感器）—— 依据现役生产库
                        // TestParameterConfig：弯曲等所有配置行 AxialPressureChannel = 0
                        pressChannelIndex: 0,
                        // 位移来源默认"位移通道2"（真机验证时读的那一路）；
                        // 通道设置页保存后会按参数表的取值即时覆盖（含双传感器两路平均）
                        displacementSource: DisplacementSource.Displace2
                    );
                });

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
                    // 网口链路（拔网线要在状态栏看得见；模拟器模式下没有这个服务 → null）
                    sp.GetService<INetworkLinkMonitor>()
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
                    sp.GetRequiredService<ICurrentSheetContext>()
                ));
                _ = services.AddSingleton<SheetSelectionPage>();

                // 历史数据页（查单 / 读明细 / 读曲线 / 导出报表）
                _ = services.AddSingleton<ITestSheetBrowser, BendingSheetBrowser>();
                // 报表预览渲染：契约在 Core、实现在 Reporting（界面只引用 Core/Control，不认识 FlexCel）
                _ = services.AddSingleton<ANX.Core.Reporting.IReportPreviewRenderer,
                    ANX.Reporting.FlexCelReportPreviewRenderer>();
                _ = services.AddSingleton<IReportExportService>(sp => new BendingReportExportService(
                    sp.GetRequiredService<TestSheetService>(),
                    AppPaths.ResolveReportRoot(),
                    new LocalizationTextResolver(sp.GetRequiredService<ILocalizationService>())
                ));
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
                    sp.GetRequiredService<ANX.Core.Reporting.IReportPreviewRenderer>()
                ));
                _ = services.AddSingleton<PrintPreviewPage>();

                // 试样编辑页 / 参数配置页（同一套参数编辑逻辑，按分组区分）
                // 试样信息填录（逐件）：字段来自 ITestExecution.SampleFields，值存明细行
                _ = services.AddSingleton(sp => new SampleInfoViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetRequiredService<ICurrentSheetContext>()
                ));
                _ = services.AddSingleton(sp => new MachineSettingsViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestParameters>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetService<ITestDeviceController>()
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

        try
        {
            // 语言与主题从**外部配置文件**读（用户 2026-10-14：改完下次启动要生效）。
            // 注意顺序：这一读必须发生在**建库之前** —— 连"库架构版本不匹配、请备份后重建"
            // 那个提示框都要用用户选的语言显示。
            await ApplyPersistedAppearanceAsync();

            // 建库 / 校验架构版本（版本不匹配会直接抛错，提示备份后重建）
            Services.GetRequiredService<DatabaseInitializer>().Initialize();

            // 启动就盯住网口链路（用户 2026-10-14）：拔网线要在界面上看得见，
            // 不能等到"跑试验才发现数据不来"；这里刻意在联机之前起，联机前拔线也认得出。
            //
            // **模拟器模式下这个服务没注册**（假设备不该报真网线的状态）→ 必须用 GetService 判空：
            // 2026-10-14 实机踩过 —— 这里原先写的是 GetRequiredService，一开模拟器就
            // "No service for type INetworkLinkMonitor has been registered" 直接起不来。
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
