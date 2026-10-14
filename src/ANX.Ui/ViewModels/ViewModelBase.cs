namespace ANX.Ui.ViewModels;

using ANX.Ui.Services;
using Wpf.Ui.Abstractions.Controls;

/// <summary>所有页面 ViewModel 的基类：提供多语言访问、导航回调与**统一的状态行**。</summary>
public abstract partial class ViewModelBase : ObservableObject, INavigationAware
{
    protected ViewModelBase(ILocalizationService localization)
    {
        Localization = localization;
    }

    protected ILocalizationService Localization { get; }

    /// <summary>
    /// 页面底部那条状态文案（就绪 / 已保存 / 失败原因…）。
    /// 写它请走 <see cref="SetStatus"/> —— 出错时把 <see cref="StatusIsError"/> 一起立起来。
    /// </summary>
    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>
    /// 这条状态是不是"错误"：界面据此把文字**变红并按每秒一次闪烁**（用户 2026-10-14）。
    /// 只有"要用户注意、否则继续不下去"的消息才算错误（含"请先选择一张试验单"这类拦下的操作）。
    /// </summary>
    [ObservableProperty]
    private bool _statusIsError;

    /// <summary>写状态行：<paramref name="isError"/> = true 时界面红字闪烁。</summary>
    protected void SetStatus(string text, bool isError = false)
    {
        Status = text;
        StatusIsError = isError;
    }

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
