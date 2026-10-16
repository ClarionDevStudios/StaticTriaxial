namespace STS.Control;

/// <summary>
/// **试验单编辑契约**（试验单弹窗用）：字段声明、读一张单、按字段新建、以及模板的存取。
///
/// 与 <see cref="ITestExecution"/> 分开，是因为它服务的是"试验单信息"这件事，
/// 与跑试验、判定、报表无关；同一个方法工程（如某具体方法）两个接口一起实现。
///
/// 口径（用户 2026-10-14 定）：
/// <list type="bullet">
///   <item>字段顺序**照现役窗体**（工程信息在前、体积指标在后）；</item>
///   <item>「试件尺寸(mm)」只是该单的**描述性记录**（进报表），判定仍用逐件试样信息的 b/h/跨径；</item>
///   <item>「试验温度 / 加载速率」是该单的**试验条件记录**：新建时用参数表当前值预填，
///         保存时**把加载速率写回参数表**（实际控制按单上填的走）；</item>
///   <item>体积指标允许留空。</item>
/// </list>
/// </summary>
public interface ITestSheetEditor
{
    /// <summary>
    /// 试验单信息字段声明（<see cref="ParameterGroup.Sheet"/> 组），**声明顺序即界面顺序**。
    /// 用 <see cref="ParameterDefinition"/> 复用既有机制：Choice 给选项、Number 给范围、Date 给日期选择器。
    /// </summary>
    IReadOnlyList<ParameterDefinition> SheetInfoFields { get; }

    /// <summary>
    /// 预览"下一个单号"（新建时**只读显示**用）。**不消耗流水** —— 真正发号在 <see cref="CreateSheetAsync"/>。
    /// </summary>
    Task<string> PeekNextSheetIdAsync(CancellationToken cancellationToken = default);

    /// <summary>读一张试验单的信息字段（键 = <see cref="SheetInfoFields"/> 的 Key）。</summary>
    Task<IReadOnlyDictionary<string, string>> ReadSheetInfoAsync(
        string sheetId,
        CancellationToken cancellationToken = default
    );

    /// <summary>按填写的字段新建一张试验单，返回单号。</summary>
    Task<string> CreateSheetAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 把表单内容**写回一张已有的试验单**（用户 2026-10-14：这是"改已有单信息"的入口）。
    /// 保存前同样做输入值验证（必填 / 数字格式 / 取值范围），不通过就抛消息让界面显示；
    /// 单号与试验时间不在可改范围（界面也不让改）。字段留空 = 把该列清成 NULL（体积指标允许留空）。
    /// </summary>
    Task UpdateSheetAsync(
        string sheetId,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 把**该单的试验条件**写回参数表（用户 2026-10-14：实际控制按单上填的加载速率走）。
    /// 新建后与"采用已有单"后各调一次；试验温度参数表里没有对应项，只留在单上做记录。
    /// </summary>
    Task PushTestConditionsAsync(string sheetId, CancellationToken cancellationToken = default);

    /// <summary>
    /// **联动缺省值**：某个字段被改成 <paramref name="value"/> 之后，方法给出"跟着变的其它字段"的值
    /// （现役口径：试验目的选常温/低温 → 预填试验温度与加载速率）。
    /// 界面只负责把返回值填进去 —— 具体哪个字段联动哪个，属方法语义，界面不认识。
    /// </summary>
    IReadOnlyDictionary<string, string> ResolveDependentDefaults(string changedKey, string value);

    /// <summary>模板表里有没有内容（决定「读取模板」能不能用）。</summary>
    Task<bool> HasTemplateAsync(CancellationToken cancellationToken = default);

    /// <summary>读模板（没有内容时回各字段默认值）。</summary>
    Task<IReadOnlyDictionary<string, string>> ReadTemplateAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>把当前表单内容存成模板（「存为模板」按钮）。</summary>
    Task SaveTemplateAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default
    );
}
