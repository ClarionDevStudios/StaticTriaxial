namespace STS.Communication;

using System.IO.Ports;

/// <summary>
/// 串口通道参数（RS232 / USB 转串口）。
/// 参数键与 IAP 版同名（PortName / BaudRate / DataBits / Parity / StopBits / Handshake），
/// 将来接入"通讯设置"页时按同一套键存取。
/// </summary>
public sealed record SerialChannelOptions(
    string PortName,
    int BaudRate = 9600,
    int DataBits = 8,
    Parity Parity = Parity.None,
    StopBits StopBits = StopBits.One,
    Handshake Handshake = Handshake.None
);
