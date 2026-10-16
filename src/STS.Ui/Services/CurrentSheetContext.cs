namespace STS.Ui.Services;

/// <summary>
/// 当前试验单上下文：试验单页选定后交给试验执行页，避免两个页面互相引用。
/// </summary>
public interface ICurrentSheetContext
{
    /// <summary>当前试验单号；未选定时为 null。</summary>
    string? SheetId { get; }

    /// <summary>当前试验单的项目名称。</summary>
    string? ProjectName { get; }

    /// <summary>登记当前试验单。</summary>
    void Set(string sheetId, string? projectName);

    /// <summary>
    /// 清空当前试验单（**换试验方法**时用）：上一方法的单在下一方法里没有意义
    /// （单号查不到、字段也对不上）。清空同样会触发 <see cref="Changed"/>，
    /// 各页面据此把单号显示与试样列表一起复位。
    /// </summary>
    void Clear();

    /// <summary>当前试验单发生变化。</summary>
    event EventHandler? Changed;
}

/// <inheritdoc />
public sealed class CurrentSheetContext : ICurrentSheetContext
{
    public string? SheetId { get; private set; }

    public string? ProjectName { get; private set; }

    public event EventHandler? Changed;

    public void Set(string sheetId, string? projectName)
    {
        SheetId = sheetId;
        ProjectName = projectName;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        SheetId = null;
        ProjectName = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
