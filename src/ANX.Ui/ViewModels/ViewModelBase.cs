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
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        OnNavigatedFrom();
        return Task.CompletedTask;
    }

    /// <summary>进入本页后调用。</summary>
    public virtual void OnNavigatedTo() { }

    /// <summary>离开本页前调用。</summary>
    public virtual void OnNavigatedFrom() { }
}
