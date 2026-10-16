namespace STS.Host;

using STS.Core.Reporting;
using STS.Ui.Services;

/// <summary>
/// 把界面的本地化服务接到报表侧的文字解析接缝（<see cref="ITextResolver"/>）上。
/// 宿主是唯一的组合根，这层桥接放在这里最省事：方法工程只认 Core 里的接口，不认识界面。
/// </summary>
internal sealed class LocalizationTextResolver(ILocalizationService localization) : ITextResolver
{
    public string Get(string key, string culture) => localization.GetString(key, culture);
}
