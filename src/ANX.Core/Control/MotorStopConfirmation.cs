namespace ANX.Core.Control;

/// <summary>
/// **停机的闭环确认**（用户 2026-10-14）：发停止命令之后，等"电机已停"从**状态回帧**得到证实。
///
/// 为什么不能"发了就当停了"：
/// <list type="bullet">
///   <item>UDP 无连接 —— 实测**链路断开时、甚至没有路由时**，<c>Send</c> 照样返回成功，
///         命令有没有到设备，只有状态回帧能回答；</item>
///   <item>就算命令到了，"电机停住"也还得看伺服/限位是否放行（电机卡住、伺服保护都会让它停不下来）。</item>
/// </list>
/// 判据用 <see cref="ITestDeviceController.IsMotorRunning"/>（机器状态位 Bit3/Bit4）：
/// 为 <c>false</c> 才算确认；为 <c>null</c>（没联机 / 没有回帧）一律算**未确认**。
/// </summary>
public static class MotorStopConfirmation
{
    /// <summary>轮询间隔。A2 状态帧约 190Hz，20ms 一拍足够灵敏，又不至于空转烧 CPU。</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>默认确认窗口：2 秒（20ms 一拍 = 100 拍，够状态位翻过来）。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 等"电机已停"被状态回帧证实。
    /// </summary>
    /// <returns>
    /// <c>true</c> = 已确认停住（含"本来就没转"）；<c>false</c> = 在 <paramref name="timeout"/> 内没等到确认。
    /// </returns>
    public static async Task<bool> WaitStoppedAsync(
        ITestDeviceController device,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(device);

        if (device.IsMotorRunning is false)
        {
            return true; // 本来就没转（或设备明确说不转），无需等
        }

        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);

            if (device.IsMotorRunning is false)
            {
                return true;
            }
        }

        return device.IsMotorRunning is false;
    }
}
