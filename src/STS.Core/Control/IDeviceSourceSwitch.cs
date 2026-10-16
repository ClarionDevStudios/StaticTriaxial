namespace STS.Core.Control;

/// <summary>
/// **数据来源可切换**的设备（真机 / 模拟）。
///
/// 用户 2026-10-15：系统设置里选"真机试验 / 模拟数据"，选完要让新的来源**立刻**生效 ——
/// 界面调 <see cref="ApplyAsync"/>（断开旧来源、按新来源联机），不用重启软件。
/// </summary>
public interface IDeviceSourceSwitch
{
    /// <summary>当前是不是在用模拟数据（界面据此决定要不要报"链路断开"这类只对真机有意义的提示）。</summary>
    bool IsSimulating { get; }

    /// <summary>
    /// 当前设备的**名字资源键**（例：模拟器 / 还没选数据来源）—— 没有键的（真机按型号报名字）回 <c>null</c>，
    /// 界面就照 <c>DeviceName</c> 原样显示。用户 2026-10-15："语言切换未处理的内容很多"。
    /// 默认实现回 <c>null</c>：只实现本接口、手里没有可选名字的替身不用管它。
    /// </summary>
    string? DeviceNameKey => null;

    /// <summary>按当前设置重新装配设备：断开旧来源、按新来源联机（旧来源从未联机过就只装配不联机）。</summary>
    Task ApplyAsync(CancellationToken cancellationToken = default);
}
