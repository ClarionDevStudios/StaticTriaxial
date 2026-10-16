namespace STS.Core.Protocol;

/// <summary>一条下发给设备的指令。指令的语义由机型编解码器解释，框架不认。</summary>
public interface ICommand
{
    /// <summary>命令字。</summary>
    byte CommandCode { get; }

    /// <summary>构造数据域（不含帧头、长度、校验）。</summary>
    byte[] BuildPayload();
}
