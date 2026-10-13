namespace ANX.Ui.ViewModels;

using ANX.Ui.Services;
using Wpf.Ui.Abstractions.Controls;

/// <summary>所有页面 ViewModel 的基类：提供多语言访问与导航回调。</summary>
public abstract class ViewModelBase : ObservableObject, INavigationAware
{
    protected ViewModelBase(ILocalizationService localization)
    {
        Localization = localization;
    }

    protected ILocalizationService Localization { get; }

    /// <summary>取本地化文案。</summary>
    protected string L(string key) => Localization.GetString(key);

    public Task OnNavigatedToAsync()
    {
        OnNavigatedTo();
        OnOpened();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        OnNavigatedFrom();
        return Task.CompletedTask;
    }

    /// <summary>进入本页后调用。</summary>
    public virtual void OnNavigatedTo() { }

    /// <summary>
    /// 页面**被显示出来**时调用。导航进入走 <see cref="OnNavigatedTo"/>、
    /// 作为**弹出窗体**弹出走 <see cref="OnOpened"/>（两条路都会调到这里，用户 2026-10-13）。
    /// 页面只实现 <see cref="OnOpened"/> 就能"当页用"与"当弹窗用"都自动刷新数据。
    /// </summary>
    public virtual void OnOpened() { }

    /// <summary>离开本页前调用。</summary>
    public virtual void OnNavigatedFrom() { }
}
