namespace ANX.Protocol.Tests.Gr3;

using System.Text;
using ANX.Protocol.Gr3;

/// <summary>下行指令的数据域字段顺序与长度（协议文档第三章）。</summary>
public sealed class Gr3DownlinkCommandTests
{
    private static readonly DateTime Sample = new(2026, 10, 10, 9, 30, 0);

    [Fact]
    public void 联机密钥应为年月日时分之和对三加一千三百一十四()
    {
        // (26 + 10 + 10 + 9 + 30) * 3 + 1314 = 85 * 3 + 1314 = 1569
        Assert.Equal(1569, Gr3DownlinkCommands.ConnectKey(Sample));
    }

    [Fact]
    public void 联机数据域应为六个时间字节加四字节大端密钥()
    {
        var command = Gr3DownlinkCommands.Connect(Sample);

        Assert.Equal(Gr3Constants.DownlinkConnect, command.CommandCode);
        Assert.Equal(
            new byte[] { 0x1A, 0x0A, 0x0A, 0x09, 0x1E, 0x00, 0x00, 0x00, 0x06, 0x21 },
            command.Payload
        );
    }

    [Fact]
    public void 试验编号应补齐到十四字节()
    {
        var command = Gr3DownlinkCommands.TestNumber("WT20261010007");
        var text = Encoding.ASCII.GetString(command.Payload);

        Assert.Equal(Gr3Constants.TestNumberLength, command.Payload.Length);
        Assert.Equal("WT20261010007 ", text); // 13 个字符 + 1 个补位空格
    }

    [Fact]
    public void 试验编号超长时应截断到十四字节()
    {
        var command = Gr3DownlinkCommands.TestNumber("ABCDEFGHIJKLMNOPQRST");
        var text = Encoding.ASCII.GetString(command.Payload);

        Assert.Equal("ABCDEFGHIJKLMN", text);
    }

    [Fact]
    public void 读设备ID应为CAH且order为二()
    {
        var command = Gr3DownlinkCommands.ReadDeviceId();

        Assert.Equal(Gr3Constants.DownlinkReadChannelConfig, command.CommandCode);
        Assert.Equal(new byte[] { 2, 0 }, command.Payload);
    }

    [Fact]
    public void 心跳应为CBH且order为一()
    {
        var command = Gr3DownlinkCommands.Heartbeat();

        Assert.Equal(Gr3Constants.DownlinkHeartbeat, command.CommandCode);
        Assert.Equal(new byte[] { 1 }, command.Payload);
    }

    [Fact]
    public void 电机加载应为方向加二字节大端速度()
    {
        var command = Gr3DownlinkCommands.MotorLoad(0x55, 500);

        Assert.Equal(new byte[] { 0x55, 0x01, 0xF4 }, command.Payload);
    }
}
