namespace ANX.Host;

using System.IO;
using System.Windows;
using System.Windows.Threading;
using ANX.Ui.Services;
using ANX.Ui.ViewModels;
using ANX.Ui.Views;
using ANX.Ui.Views.Pages;
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

            await AppHost.StartAsync();

            logger.LogInformation("启动完成，日志目录：{LogDirectory}", AppPaths.ResolveLogDirectory());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "启动失败");
            throw;
        }
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
