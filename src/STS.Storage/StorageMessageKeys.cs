namespace STS.Storage;

/// <summary>
/// 存储层抛给界面的**消息资源键**（用户 2026-10-15：语言切换要把这些也盖住）。
/// 存储层不认识语言：异常里带键 + 中文兜底，界面按当前语言取译文（见 <c>STS.Ui.Services.UiText</c>）。
///
/// 注意：`方法 X 的表没有声明列 Y` 那几条属**编程错误**（列名拼错），是给开发看的，没接键。
/// </summary>
public static class StorageMessageKeys
{
    /// <summary>找不到试验单：{0} = 单号。</summary>
    public const string SheetNotFound = "Msg.Storage.SheetNotFound";

    /// <summary>找不到单号对应的试验单：{0} = 单号。</summary>
    public const string SheetNotFoundByNumber = "Msg.Storage.SheetNotFoundByNumber";

    /// <summary>未注册的方法代号：{0} = 代号。</summary>
    public const string MethodNotRegistered = "Msg.Storage.MethodNotRegistered";

    /// <summary>单号流水用尽：{0} = 前缀、{1} = 日期。</summary>
    public const string NumberExhausted = "Msg.Storage.NumberExhausted";

    /// <summary>全部键 —— 供语言字典一致性用例把守。</summary>
    public static readonly string[] All =
    [
        SheetNotFound,
        SheetNotFoundByNumber,
        MethodNotRegistered,
        NumberExhausted,
    ];
}
