namespace ANX.Core.Reporting;

/// <summary>
/// 资源键 → 文本（可指定语言）。报表装配用它取可见文字（曲线图的轴名、图题等）——
/// **方法只声明资源键，文案留在界面层的语言资源里**（IAP 的同款做法：
/// `FieldDefinition.DisplayNameKey` 是键不是文本）。
/// 由宿主把界面的本地化服务接上来；无界面场景（测试）可传 null，此时退回键名。
/// </summary>
public interface ITextResolver
{
    /// <summary>取 <paramref name="key"/> 在 <paramref name="culture"/> 下的译文。</summary>
    string Get(string key, string culture);
}
