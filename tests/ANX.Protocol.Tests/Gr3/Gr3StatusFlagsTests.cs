namespace ANX.Protocol.Tests.Gr3;

using ANX.Protocol.Gr3;

/// <summary>
/// A2H 状态寄存器的位定义（协议文档 2.3 节）。逐位断言，避免以后有人"顺手改位序"。
/// </summary>
public sealed class Gr3StatusFlagsTests
{
    [Fact]
    public void 控制器状态六位的取值应为一二四八十六三十二()
    {
        Assert.Equal(0x01, (byte)Gr3MachineStatus.PressureOverload);
        Assert.Equal(0x02, (byte)Gr3MachineStatus.DisplacementUpperLimit);
        Assert.Equal(0x04, (byte)Gr3MachineStatus.DisplacementLowerLimit);
        Assert.Equal(0x08, (byte)Gr3MachineStatus.MotorForward);
        Assert.Equal(0x10, (byte)Gr3MachineStatus.MotorReverse);
        Assert.Equal(0x20, (byte)Gr3MachineStatus.TemperatureControlActive);
    }

    [Fact]
    public void 试验状态四位的取值应为一二四八()
    {
        Assert.Equal(0x01, (byte)Gr3TestStatus.Running);
        Assert.Equal(0x02, (byte)Gr3TestStatus.Paused);
        Assert.Equal(0x04, (byte)Gr3TestStatus.BreakPointFound);
        Assert.Equal(0x08, (byte)Gr3TestStatus.Holding);
    }

    [Fact]
    public void 组合值应能按位读出语义()
    {
        // 真机抓到的 0x12：位移上限位 + 电机反转（下降）运行中
        var status = (Gr3MachineStatus)0x12;

        Assert.True(status.HasFlag(Gr3MachineStatus.DisplacementUpperLimit));
        Assert.True(status.HasFlag(Gr3MachineStatus.MotorReverse));
        Assert.False(status.HasFlag(Gr3MachineStatus.PressureOverload));
        Assert.False(status.HasFlag(Gr3MachineStatus.DisplacementLowerLimit));
        Assert.False(status.HasFlag(Gr3MachineStatus.MotorForward));
        Assert.False(status.HasFlag(Gr3MachineStatus.TemperatureControlActive));
    }

    [Fact]
    public void 试验状态运行中与破碎点应能同时成立()
    {
        var status = Gr3TestStatus.Running | Gr3TestStatus.BreakPointFound;

        Assert.True(status.HasFlag(Gr3TestStatus.Running));
        Assert.True(status.HasFlag(Gr3TestStatus.BreakPointFound));
        Assert.False(status.HasFlag(Gr3TestStatus.Paused));
        Assert.False(status.HasFlag(Gr3TestStatus.Holding));
    }
}
