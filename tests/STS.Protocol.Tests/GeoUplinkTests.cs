namespace STS.Protocol.Tests;

using System.Buffers.Binary;
using STS.Protocol.Geo;

/// <summary>A0 / A1 / A2 解析（字段表按 V1.7 第 5.5 ~ 5.7 节）。</summary>
public sealed class GeoUplinkTests
{
    [Fact]
    public void 应解析A0系统配置()
    {
        var data = new byte[34];
        data[0] = 10; // 1.0
        data[1] = 15; // 1.5
        data[2] = 17; // 1.7
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(3), 100000);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(7), 10000);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(11), 25000);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(15), 25000);
        data[19] = 2;
        data[20] = 2;
        data[21] = 3;
        data[22] = 3;
        data[23] = 1;
        data[24] = 1;
        data[25] = 0;
        data[26] = 0;
        data[27] = 1;
        data[28] = 0;
        data[29] = 1;
        data[30] = 1;
        data[31] = 1;
        data[32] = 0;
        data[33] = 1;

        var parsed = GeoSystemConfig.TryParse(new GeoFrame(0xA0, data), out var config);

        Assert.True(parsed);
        Assert.NotNull(config);
        Assert.Equal(1.0, config.HardwareVersion, 9);
        Assert.Equal(1.5, config.FirmwareVersion, 9);
        Assert.Equal(1.7, config.ProtocolVersion, 9);
        Assert.Equal(100000, config.Press1Scale);
        Assert.Equal(25000, config.Displacement2Scale);
        Assert.Equal(2, config.Press1Digits);
        Assert.Equal(3, config.Displacement1Digits);
        Assert.True(config.Displacement3Valid);
        Assert.False(config.Displacement4Valid);
        Assert.True(config.Press3Valid);
        Assert.False(config.Press4Valid);
        Assert.True(config.Press5Valid);
    }

    [Fact]
    public void 应解析A1实时数据含V17体变四字段()
    {
        var data = new byte[60];
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), 123456);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), -1);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), 306123);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), -12345);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(16), 1);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(18), 2);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(20), 3);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(22), 4);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(24), 30); // 围压 0.030MPa = 30kPa
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(26), -5);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(28), 7);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(30), (1u << 10) | (1u << 11));
        data[34] = 0x08; // 电机正转
        data[35] = 0x01; // 试验运行
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(36), -1234); // -1.234ml
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(40), 1000);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(44), 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(48), 250); // 0.250ml
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(52), 11);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(56), 22);

        var parsed = GeoRealtimeSample.TryParse(new GeoFrame(0xA1, data), out var sample);

        Assert.True(parsed);
        Assert.NotNull(sample);
        Assert.True(sample.HasVolumes);
        Assert.Equal(123456, sample.Press1Raw);
        Assert.Equal(-1, sample.Press2Raw);
        Assert.Equal(306123, sample.Displacement1Raw);
        Assert.Equal(30, sample.Press3Raw);
        Assert.Equal(0.030, sample.ConfiningPressureMPa, 9);
        Assert.Equal(-0.005, sample.BackPressureMPa, 9);
        Assert.True(sample.MotorUp);
        Assert.False(sample.MotorDown);
        Assert.True(sample.TestRunning);
        Assert.True(sample.ConfiningStable);
        Assert.True(sample.BackStable);
        Assert.Equal(-1.234, sample.ConfiningVolumeML, 9);
        Assert.Equal(1.0, sample.BackVolumeML, 9);
        Assert.Equal(0.25, sample.DrainVolumeML, 9);
        Assert.Equal(11, sample.Data1Raw);
        Assert.Equal(22, sample.Data2Raw);
    }

    [Fact]
    public void 老固件短帧应可解析且体变按零处理()
    {
        var data = new byte[36]; // 到 TestStatus 为止，无 V1.7 体变四字段
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(24), 60);

        var parsed = GeoRealtimeSample.TryParse(new GeoFrame(0xA1, data), out var sample);

        Assert.True(parsed);
        Assert.NotNull(sample);
        Assert.False(sample.HasVolumes);
        Assert.Equal(60, sample.Press3Raw);
        Assert.Equal(0.0, sample.DrainVolumeML, 9);
    }

    [Fact]
    public void 应解析A2应答()
    {
        Assert.True(GeoAck.TryParse(new GeoFrame(0xA2, [1]), out var ok));
        Assert.Equal(GeoAckStatus.Success, ok!.Status);

        Assert.True(GeoAck.TryParse(new GeoFrame(0xA2, [0]), out var crcError));
        Assert.Equal(GeoAckStatus.CrcError, crcError!.Status);

        Assert.True(GeoAck.TryParse(new GeoFrame(0xA2, [2]), out var rejected));
        Assert.Equal(GeoAckStatus.CannotExecute, rejected!.Status);
    }

    [Fact]
    public void 命令不符或长度不足应解析失败()
    {
        Assert.False(GeoSystemConfig.TryParse(new GeoFrame(0xA1, new byte[34]), out _));
        Assert.False(GeoRealtimeSample.TryParse(new GeoFrame(0xA0, new byte[60]), out _));
        Assert.False(GeoRealtimeSample.TryParse(new GeoFrame(0xA1, new byte[10]), out _));
        Assert.False(GeoAck.TryParse(new GeoFrame(0xA1, [1]), out _));
    }
}
