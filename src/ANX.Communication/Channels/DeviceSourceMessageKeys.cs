namespace ANX.Communication.Channels;

/// <summary>
/// 数据来源的**设备名**资源键（用户 2026-10-15：语言切换要把状态行里的设备名也盖住）。
/// 真机报的是自己的型号名（不是界面文案），不挂键、原样显示。
/// </summary>
public static class DeviceSourceMessageKeys
{
    /// <summary>模拟器。</summary>
    public const string Simulator = "Device.Name.Simulator";

    /// <summary>还没选数据来源。</summary>
    public const string NoSource = "Device.Name.NoSource";

    /// <summary>全部键 —— 供语言字典一致性用例把守。</summary>
    public static readonly string[] All = [Simulator, NoSource];
}
