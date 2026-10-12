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
using ANX.Control;
using ANX.Core.Control;
using ANX.Core.Methods;
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
    private const string DeviceHost = "192.168.0.122";

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

                // 导航
                _ = services.AddSingleton<INavigationService, NavigationService>();
                _ = services.AddSingleton<INavigationWindow, ShellWindow>();

                // 视图与 ViewModel
                _ = services.AddSingleton<ShellViewModel>();
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
                _ = services.AddSingleton<ITestExecution, BendingExecution>();

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
                        displaceChannelIndex: 1
                    );
                });

                _ = services.AddSingleton(sp => new TestExecutionViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetService<ITestDeviceController>(),
                    sp.GetRequiredService<ICurrentSheetContext>(),
                    sp.GetRequiredService<INavigationService>(),
                    sp.GetRequiredService<IReportExportService>()
                ));
                _ = services.AddSingleton<TestExecutionPage>();

                // 当前试验单上下文 + 试验单页
                _ = services.AddSingleton<ICurrentSheetContext, CurrentSheetContext>();
                _ = services.AddSingleton(sp => new SheetSelectionViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetRequiredService<ITestSheetBrowser>(),
                    sp.GetRequiredService<ICurrentSheetContext>(),
                    sp.GetRequiredService<INavigationService>()
                ));
                _ = services.AddSingleton<SheetSelectionPage>();

                // 历史数据页（查单 / 读明细 / 读曲线 / 导出报表）
                _ = services.AddSingleton<ITestSheetBrowser, BendingSheetBrowser>();
                _ = services.AddSingleton<IReportExportService>(sp => new BendingReportExportService(
                    sp.GetRequiredService<TestSheetService>(),
                    AppPaths.ResolveReportRoot(),
                    new LocalizationTextResolver(sp.GetRequiredService<ILocalizationService>())
                ));
                _ = services.AddSingleton(sp => new HistoryViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestSheetBrowser>(),
                    sp.GetRequiredService<IReportExportService>(),
                    sp.GetRequiredService<ITestExecution>()
                ));
                _ = services.AddSingleton<HistoryPage>();

                // 试样编辑页 / 参数配置页（同一套参数编辑逻辑，按分组区分）
                // 试样信息填录（逐件）：字段来自 ITestExecution.SampleFields，值存明细行
                _ = services.AddSingleton(sp => new SampleInfoViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestExecution>(),
                    sp.GetRequiredService<ICurrentSheetContext>()
                ));
                _ = services.AddSingleton(sp => new MachineSettingsViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestParameters>()
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
            // M1 先按固定默认启动；设置持久化（IAppSettingsService 的实现）是后续步骤。
            Services.GetRequiredService<ILocalizationService>().SetLanguage("zh-CN");
            Services.GetRequiredService<IAppThemeService>().Apply("Dark");

            // 建库 / 校验架构版本（版本不匹配会直接抛错，提示备份后重建）
            Services.GetRequiredService<DatabaseInitializer>().Initialize();

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
