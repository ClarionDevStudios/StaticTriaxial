namespace STS.Core;

/// <summary>
/// 记录唯一标识（UUID）的生成口径。
///
/// **为什么要它**：试验单与试样（明细）都要带 UUID —— 现役把数据上传云端、按 UUID 去重，
/// 免得同一张单被传两次（同号单在云库里变成两条）。用户 2026-10-13 明确：
/// 试验单表生成 UUID，**试样信息表沿用同一张试验单的 UUID**。
///
/// **格式与现役 <c>GenerateCleanGUID</c> 一致**：标准 GUID 文本去掉两侧花括号，大写 ——
/// 36 个字符、带连字符（如 <c>3F2504E0-4F89-11D3-9A0C-0305E82C3301</c>）；
/// 现役另有 <c>GetGUIDKey</c>（去连字符的 32 位变体），本软件不用它。
/// </summary>
public static class RecordUuid
{
    /// <summary>生成一个新的 UUID（大写、带连字符、无花括号）。</summary>
    public static string New() => Guid.NewGuid().ToString("D").ToUpperInvariant();
}
