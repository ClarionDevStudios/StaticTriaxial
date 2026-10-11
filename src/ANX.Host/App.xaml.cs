namespace ANX.Host;

using System.IO;
using System.Windows;
using System.Windows.Threading;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using ANX.Ui.Views;
using ANX.Ui.Views.Pages;
using ANX.Communication.Channels;
using ANX.Control;
using ANX.Core.Control;
using ANX.Core.Methods;
using ANX.Method.Bending;
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
                Path.Combine(logDirectory, "anx-.log"),
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

#if SIMULATOR
                _ = services.AddSingleton<ITestDeviceController>(new SimulatedDeviceController(
                    sampleIntervalSeconds: 0.02,
                    timeScale: 5.0
                ));
#endif

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
                    AppPaths.ResolveReportRoot()
                ));
                _ = services.AddSingleton(sp => new HistoryViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestSheetBrowser>(),
                    sp.GetRequiredService<IReportExportService>()
                ));
                _ = services.AddSingleton<HistoryPage>();

                // 试样编辑页 / 参数配置页（同一套参数编辑逻辑，按分组区分）
                _ = services.AddSingleton(sp => new SampleEditorViewModel(
                    sp.GetRequiredService<ILocalizationService>(),
                    sp.GetRequiredService<ITestParameters>()
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
        }
        catch (SchemaVersionMismatchException ex)
        {
            // 可预期的失败：把"哪个库、差多少、下一步做什么"讲清楚，然后**正常退出**。
            // 不向上抛 —— 否则 WPF 的未处理异常会再盖一个原始错误框，用户还是看不懂。
            logger.LogError(ex, "启动失败：数据库架构版本不匹配");
            ShowSchemaMismatchDialog(ex);
            Shutdown(1);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动失败");
            throw;
        }
    }

    /// <summary>
    /// 建库版本对不上时的提示框：库版本 / 程序要求 / 要备份的文件，逐条给出。
    /// 这里用系统 MessageBox 而不是 WPF-UI 的对话框 —— 此刻外壳还没建起来（没有可用的
    /// 宿主窗口与内容对话框宿主），而这条提示必须在任何窗口之前就能弹出来。
    /// </summary>
    private void ShowSchemaMismatchDialog(SchemaVersionMismatchException mismatch)
    {
        var localization = Services.GetRequiredService<ILocalizationService>();

        // 换行在代码里拼：XAML/BAML 会把资源文本里的 CR/LF 归一化掉，写进资源留不住
        var text = string.Join(
            Environment.NewLine,
            localization.GetString("Startup.SchemaMismatch.Summary"),
            string.Empty,
            string.Format(
                localization.GetString("Startup.SchemaMismatch.Versions"),
                mismatch.DatabaseVersion,
                mismatch.RequiredVersion
            ),
            string.Empty,
            localization.GetString("Startup.SchemaMismatch.Hint"),
            mismatch.DatabasePath
        );

        MessageBox.Show(
            text,
            localization.GetString("Startup.SchemaMismatch.Title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning
        );
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        Log.Information("退出");

        await AppHost.StopAsync();
        AppHost.Dispose();
        await Log.CloseAndFlushAsync();
    }

    private void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e
    )
    {
        Log.Error(e.Exception, "界面线程未处理异常");
    }
}
