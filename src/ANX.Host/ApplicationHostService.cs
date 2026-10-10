namespace ANX.Host;

using System.Windows;
using ANX.Ui.Views;
using ANX.Ui.Views.Pages;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;

/// <summary>宿主生命周期：启动时开主窗口并导航到首屏，退出时关窗。</summary>
internal sealed class ApplicationHostService(IServiceProvider serviceProvider) : IHostedService
{
    private INavigationWindow? _navigationWindow;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Application.Current.Windows.OfType<ShellWindow>().Any())
        {
            return Task.CompletedTask;
        }

        _navigationWindow = (INavigationWindow)
            serviceProvider.GetService(typeof(INavigationWindow))!;
        _navigationWindow.ShowWindow();
        _navigationWindow.Navigate(typeof(HomePage));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _navigationWindow?.CloseWindow();
        return Task.CompletedTask;
    }
}
