namespace ANX.Communication.NetworkLink;

/// <summary>
/// 链路状态细节的**资源键**（用户 2026-10-15：语言切换要把状态栏与断链报警里的文字也盖住）。
/// 监视器不认识语言：状态里带键 + 中文兜底（见 <c>ANX.Core.NetworkLinkState.DetailText</c>），
/// 界面用 <c>ANX.Ui.Services.UiText.Describe(state)</c> 取当前语言。
/// </summary>
public static class NetworkLinkMessageKeys
{
    /// <summary>还没检测过。</summary>
    public const string NotChecked = "Link.Detail.NotChecked";

    /// <summary>读网卡状态失败：{0} = 原因。</summary>
    public const string ReadFailed = "Link.Detail.ReadFailed";

    /// <summary>没找到能到达 {0} 的网口。</summary>
    public const string AdapterMissing = "Link.Detail.AdapterMissing";

    /// <summary>链路正常（{0} = 速率）。</summary>
    public const string Up = "Link.Detail.Up";

    /// <summary>网口 Down。</summary>
    public const string MediaDisconnected = "Link.Detail.MediaDisconnected";

    /// <summary>网口 Dormant（没有载波）。</summary>
    public const string Dormant = "Link.Detail.Dormant";

    /// <summary>网卡不存在。</summary>
    public const string NotPresent = "Link.Detail.NotPresent";

    /// <summary>其它网口状态：{0} = 系统给的状态名。</summary>
    public const string Other = "Link.Detail.Other";

    /// <summary>全部键 —— 供语言字典一致性用例把守。</summary>
    public static readonly string[] All =
    [
        NotChecked,
        ReadFailed,
        AdapterMissing,
        Up,
        MediaDisconnected,
        Dormant,
        NotPresent,
        Other,
    ];
}
