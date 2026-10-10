namespace ANX.Protocol.Gr3;

/// <summary>PC → GR-3 的下行指令构造。字段顺序与长度按协议文档第三章。</summary>
public static class Gr3DownlinkCommands
{
    /// <summary>联机密钥：（年 + 月 + 日 + 时 + 分）× 3 + 1314。年份取两位。</summary>
    public static int ConnectKey(DateTime now) =>
        ((now.Year % 100) + now.Month + now.Day + now.Hour + now.Minute) * 3 + 1314;

    /// <summary>C0H 联机：年、月、日、时、分、秒（各 1 字节）+ 密钥（Int32）。</summary>
    public static Gr3Command Connect(DateTime now)
    {
        var payload = new Gr3CommandWriter()
            .WriteByte((byte)(now.Year % 100))
            .WriteByte((byte)now.Month)
            .WriteByte((byte)now.Day)
            .WriteByte((byte)now.Hour)
            .WriteByte((byte)now.Minute)
            .WriteByte((byte)now.Second)
            .WriteInt32(ConnectKey(now))
            .ToArray();

        return new Gr3Command(Gr3Constants.DownlinkConnect, payload);
    }

    /// <summary>C1H 进入试验模式。</summary>
    public static Gr3Command TestMode(
        byte function,
        byte recordMode,
        ushort recordInterval,
        byte recordStart,
        byte pressChannel,
        byte displaceChannel
    )
    {
        var payload = new Gr3CommandWriter()
            .WriteByte(function)
            .WriteByte(recordMode)
            .WriteUInt16(recordInterval)
            .WriteByte(recordStart)
            .WriteByte(pressChannel)
            .WriteByte(displaceChannel)
            .ToArray();

        return new Gr3Command(Gr3Constants.DownlinkTestMode, payload);
    }

    /// <summary>C2H 设置试验编号（14 字节 ASCII，不足补空格）。</summary>
    public static Gr3Command TestNumber(string testNumber)
    {
        var payload = new Gr3CommandWriter()
            .WriteAscii(testNumber, Gr3Constants.TestNumberLength)
            .ToArray();

        return new Gr3Command(Gr3Constants.DownlinkTestNumber, payload);
    }

    /// <summary>C3H 数据清零。</summary>
    public static Gr3Command Zero(byte pressFlags, byte displaceFlags) =>
        new(Gr3Constants.DownlinkZero, [pressFlags, displaceFlags]);

    /// <summary>
    /// C4H 电机控制：方向字 + 速度。
    /// 方向字取值见 <see cref="Gr3Constants.MotorOrderStop"/> / <see cref="Gr3Constants.MotorOrderForwardUp"/> /
    /// <see cref="Gr3Constants.MotorOrderReverseDown"/>（**0 停止 / 1 正转上升 / 2 反转下降**）。
    /// 设备行为：同向运转时只改变速度；反向时先停止再以当前速度反向。
    /// </summary>
    public static Gr3Command MotorLoad(byte order, ushort speed)
    {
        var payload = new Gr3CommandWriter().WriteByte(order).WriteUInt16(speed).ToArray();
        return new Gr3Command(Gr3Constants.DownlinkMotorLoad, payload);
    }

    /// <summary>
    /// C5H **试验控制指令**：命令 + 速度。
    /// 注意：协议目录把它写成"电机控制命令"，实际是试验控制（2026-10-10 用户澄清，以此为准）。
    /// **复位不在这里** —— 复位发 C3H 清零。
    /// </summary>
    public static Gr3Command TestControl(byte order, ushort speed)
    {
        var payload = new Gr3CommandWriter().WriteByte(order).WriteUInt16(speed).ToArray();
        return new Gr3Command(Gr3Constants.DownlinkMotorControl, payload);
    }

    /// <summary>CAH 读取：order=1 读通道配置（回 A3），order=2 读设备 ID（回 A4）。</summary>
    public static Gr3Command ReadChannelConfig(byte order, byte channelId) =>
        new(Gr3Constants.DownlinkReadChannelConfig, [order, channelId]);

    /// <summary>读设备唯一 ID（CAH，order=2）。</summary>
    public static Gr3Command ReadDeviceId(byte channelId = 0) => ReadChannelConfig(2, channelId);

    /// <summary>CBH 心跳（Order=1）。</summary>
    public static Gr3Command Heartbeat() => new(Gr3Constants.DownlinkHeartbeat, [1]);
}
