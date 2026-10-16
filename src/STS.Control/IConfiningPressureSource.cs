namespace STS.Control;

/// <summary>
/// **围压目标值的来源**（可选接口，用户 2026-10-16 的分段流程配套）：
/// 执行页的「施加围压」按钮要知道"这一件该加多少 σ3"，但 σ3 是**具体方法的试样信息字段**
/// （T 1236 是 `ConfiningPressure`）—— 界面不认识方法字段，就走这个接缝向方法要：
/// 没记录 / 解析不了回 <c>null</c>，界面按"先去填试样信息"提示。
///
/// 与 <see cref="STS.Core.Control.IConfiningPressureControl"/>（设备侧发 C2）分开：
/// 一个管"加多少"（方法），一个管"怎么发"（设备）。
/// </summary>
public interface IConfiningPressureSource
{
    /// <summary>该件的试验围压目标（kPa）：**只认实存数据**（没填过回 null，不拿字段默认值兜）。</summary>
    Task<double?> ReadConfiningTargetKPaAsync(
        string sheetId,
        string sampleId,
        CancellationToken cancellationToken = default
    );
}
