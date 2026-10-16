namespace STS.Storage;

/// <summary>
/// 数据库架构版本与程序要求不一致。这是**可预期**的失败（本项目不做迁移，
/// 正确处置是"备份旧库后重建"），所以单独成型，把三个事实随异常带出来 ——
/// 上层（宿主）据此能把"哪个库、差多少、下一步做什么"直接讲给用户，
/// 而不必去解析错误文本。
/// </summary>
public sealed class SchemaVersionMismatchException(
    int databaseVersion,
    int requiredVersion,
    string databasePath
) : InvalidOperationException(
    $"数据库架构版本不匹配（库为 {databaseVersion}，程序要求 {requiredVersion}）。"
        + "本项目不做迁移：请备份旧库后重建。"
)
{
    /// <summary>库里记录的架构版本。</summary>
    public int DatabaseVersion { get; } = databaseVersion;

    /// <summary>程序要求的架构版本。</summary>
    public int RequiredVersion { get; } = requiredVersion;

    /// <summary>对不上的那个库文件路径（提示用户备份的就是它）。</summary>
    public string DatabasePath { get; } = databasePath;
}
