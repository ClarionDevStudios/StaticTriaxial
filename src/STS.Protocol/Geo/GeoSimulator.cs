namespace STS.Protocol.Geo;

using System.Buffers.Binary;

/// <summary>模拟下位机的可调参数（全部为"够用"的模拟约定，真机口径以现场为准）。</summary>
/// <param name="InitialDisplacementMM">开机位移（mm）；荷载从该点起算（压头接触前为 0）。</param>
/// <param name="LoadSlopeKNPerMM">荷载斜率（kN/mm）——保证"每 0.5mm 的轴向应力增量"明显大于 5kPa，
/// 免得全链路跑到一半被平台的停止判据先拦下（Φ100 试件下约 10kPa/0.5mm）。</param>
/// <param name="ConfiningRampKPaPerSecond">围压向目标值缓升的速率（kPa/s）。</param>
public sealed record GeoSimulatorOptions(
    double InitialDisplacementMM = 10.0,
    double LoadSlopeKNPerMM = 0.16,
    double ConfiningRampKPaPerSecond = 100.0
);

/// <summary>
/// 土工三轴（GR-1）**模拟下位机**：按 V1.7 同一套编解码应答下行指令（C0 / C1 / C2 / C3 → A0 / A2），
/// 并按拍产出 A1 实时流；数据形状照 T 1236（围压缓升 → 轴向加载近似线性上升）。
/// 供"没有真机"时跑通全链路。**只在 Debug 构建里编译**（Release 由 csproj 排除）。
///
/// 模拟约定（与真机的换算口径是分开的两件事；真机换算在 `GeoDeviceController` 落地时对照现役确认）：
/// - PRESS1/位移原始值：**÷10³ = kN / mm**（即原始值 0.001kN / 0.001mm，与 A0 里声明的 3 位小数一致）；
/// - PRESS3（围压）：单位 0.001MPa，**原始值 = kPa**（30kPa → 30）；
/// - 时间：<see cref="Tick"/> 由调用方给"经过的秒数"（回环通道按节拍 ×时间倍率喂）。
/// </summary>
public sealed class GeoSimulator
{
    private readonly GeoSimulatorOptions _options;
    private readonly GeoFrameAssembler _assembler = new();

    private bool _online;
    private bool _streaming;
    private bool _testRunning;
    private byte _motorOrder;
    private int _motorSpeedRaw;
    private double _displacementMM;
    private double _confiningKPa;
    private double _confiningTargetKPa;
    private bool _confiningEnabled;

    public GeoSimulator(GeoSimulatorOptions? options = null)
    {
        _options = options ?? new GeoSimulatorOptions();
        _displacementMM = _options.InitialDisplacementMM;
    }

    /// <summary>是否已联机（收到过 C0 ‘O’,1）。</summary>
    public bool IsOnline => _online;

    /// <summary>是否处于"主动回送 A1"（收到过 C0 ‘O’,2）。</summary>
    public bool IsStreaming => _streaming;

    /// <summary>喂入一段字节（PC → 设备），返回需要回送的字节（可能为空）。</summary>
    public byte[] Feed(ReadOnlySpan<byte> bytes)
    {
        var responses = new List<byte>();

        foreach (var frame in _assembler.Feed(bytes))
        {
            var response = Handle(frame);

            if (response is not null)
            {
                responses.AddRange(response);
            }
        }

        return responses.ToArray();
    }

    /// <summary>
    /// 推进 <paramref name="deltaSeconds"/> 秒并（若在主动回送）产出一帧 A1。
    /// 测试里用固定步长驱动、应用里由回环通道按节拍 ×时间倍率驱动。
    /// </summary>
    public byte[] Tick(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return [];
        }

        // 围压缓升（到目标附近吸附并置"稳压"）
        if (_confiningEnabled && Math.Abs(_confiningTargetKPa - _confiningKPa) > 0.5)
        {
            var step = _options.ConfiningRampKPaPerSecond * deltaSeconds;
            _confiningKPa += Math.Sign(_confiningTargetKPa - _confiningKPa) * step;

            if (Math.Abs(_confiningTargetKPa - _confiningKPa) <= 0.5)
            {
                _confiningKPa = _confiningTargetKPa;
            }
        }

        // 电机推进位移
        if (_motorOrder != 0)
        {
            var speedMMPerMinute = _motorSpeedRaw / 1000.0;
            var delta = speedMMPerMinute / 60.0 * deltaSeconds;

            _displacementMM = _motorOrder == 1
                ? _displacementMM + delta
                : Math.Max(0, _displacementMM - delta);
        }

        return _streaming ? GeoFrameCodec.Build(GeoCommands.RealtimeCommand, BuildRealtimeData()) : [];
    }

    private byte[]? Handle(GeoFrame frame) =>
        frame.Command switch
        {
            GeoCommands.ConnectCommand => HandleConnect(frame.Data),
            GeoCommands.MotorCommand => HandleMotor(frame.Data),
            GeoCommands.PressureCommand => HandlePressure(frame.Data),
            GeoCommands.TestStateCommand => HandleTestState(frame.Data),
            _ => BuildAck(GeoAckStatus.CannotExecute),
        };

    private byte[]? HandleConnect(byte[] data)
    {
        // 格式：'O' + order（现役同字节：0x4F）
        if (data.Length < 2 || data[0] != 0x4F)
        {
            return BuildAck(GeoAckStatus.CannotExecute);
        }

        switch (data[1])
        {
            case 1:
                _online = true;
                return BuildSystemConfig();

            case 2:
                if (!_online)
                {
                    return BuildAck(GeoAckStatus.CannotExecute);
                }

                _streaming = true;
                return BuildAck(GeoAckStatus.Success);

            default:
                return BuildAck(GeoAckStatus.CannotExecute);
        }
    }

    private byte[]? HandleMotor(byte[] data)
    {
        // 格式：order1 + speed(hi) + speed(lo)
        if (data.Length < 3 || data[0] > 2)
        {
            return BuildAck(GeoAckStatus.CannotExecute);
        }

        _motorOrder = data[0];
        _motorSpeedRaw = (data[1] << 8) | data[2];

        return BuildAck(GeoAckStatus.Success);
    }

    private byte[]? HandlePressure(byte[] data)
    {
        // 26 字节版：目标值 4B 大端 ×2 + 启停 ×2 + 速度 2B×3 + 加载杆面积 4B
        if (data.Length < 20)
        {
            return BuildAck(GeoAckStatus.CannotExecute);
        }

        _confiningTargetKPa = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0));
        _confiningEnabled = data[8] != 0;

        if (!_confiningEnabled)
        {
            _confiningTargetKPa = 0;
        }

        return BuildAck(GeoAckStatus.Success);
    }

    private byte[]? HandleTestState(byte[] data)
    {
        // 格式：'T'/'S' + 动作
        if (data.Length < 2)
        {
            return BuildAck(GeoAckStatus.CannotExecute);
        }

        switch (data[0])
        {
            case 0x54: // 'T'
                _testRunning = data[1] == 1;
                return BuildAck(GeoAckStatus.Success);

            case 0x53: // 'S'（饱和，T1236 用不到；接受但不动作）
                return BuildAck(GeoAckStatus.Success);

            default:
                return BuildAck(GeoAckStatus.CannotExecute);
        }
    }

    private static byte[] BuildAck(GeoAckStatus status) =>
        GeoFrameCodec.Build(GeoCommands.AckCommand, [(byte)status]);

    private byte[] BuildSystemConfig()
    {
        var data = new byte[34];
        data[0] = 10; // 硬件 1.0
        data[1] = 17; // 固件 1.7
        data[2] = 17; // 规约 1.7
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(3), 30000); // PRESS1 量程（模拟约定）
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(7), 30000); // PRESS2
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(11), 25000); // 位移1（0.001mm）
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(15), 25000); // 位移2
        data[19] = 3; // PRESS1 小数位
        data[20] = 3;
        data[21] = 3; // 位移1 小数位
        data[22] = 3;
        data[23] = 1; // PRESS1 单位
        data[24] = 1;
        data[25] = 0;
        data[26] = 0;
        // 千分表 3-6 未安装；围压已装，反压 / 孔压未装
        data[27] = 0;
        data[28] = 0;
        data[29] = 0;
        data[30] = 0;
        data[31] = 1; // PRESS3（围压）有效
        data[32] = 0;
        data[33] = 0;

        return GeoFrameCodec.Build(GeoCommands.SystemConfigCommand, data);
    }

    private byte[] BuildRealtimeData()
    {
        var data = new byte[60];

        var deformationMM = Math.Max(0, _displacementMM - _options.InitialDisplacementMM);
        var loadKN = _options.LoadSlopeKNPerMM * deformationMM;

        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(0), (int)Math.Round(loadKN * 1000.0));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(4), 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(8), (int)Math.Round(_displacementMM * 1000.0));
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(12), (int)Math.Round(_displacementMM * 1000.0));
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(16), 0);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(18), 0);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(20), 0);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(22), 0);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(24), (short)Math.Round(_confiningKPa));
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(26), 0);
        BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(28), 0);

        uint pressureStatus = 0;

        if (_confiningEnabled)
        {
            if (Math.Abs(_confiningTargetKPa - _confiningKPa) <= 0.5)
            {
                pressureStatus |= 1u << 10; // 围压稳压
            }
            else
            {
                pressureStatus |= 1u << 0; // 围压加载中
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(30), pressureStatus);

        byte mainStatus = 0;

        if (_motorOrder == 1)
        {
            mainStatus |= 0x08; // 电机正转（上升）
        }
        else if (_motorOrder == 2)
        {
            mainStatus |= 0x10; // 电机反转（下降）
        }

        data[34] = mainStatus;
        data[35] = (byte)(_testRunning ? 0x01 : 0x00);

        // 体变四字段（V1.7）：模拟器先给 0（先解析、入库后定）
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(36), 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(40), 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(44), 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(48), 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(52), 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(56), 0);

        return data;
    }
}
