namespace STS.Ui.Views;

using System.Windows.Controls;
using System.Windows.Data;

/// <summary>
/// 「试验单列表」那四列（历史数据页与试验单弹窗共用）。
///
/// **列头必须在这里建，不能写在 XAML 上**：`DataGridColumn` 不在可视化树里，
/// XAML 里的 <c>{DynamicResource}</c> 查不到 / 换语言后也不会刷新
/// （用户 2026-10-15："试验单编辑页面，选中其他项目时，试验单列表的列标题没翻译"）。
/// 所以列头文字按当前语言现取，换语言时整列重建（页面调 <see cref="Build"/>）。
/// </summary>
public static class SheetListColumns
{
    /// <summary>按当前语言重建四列（调用方负责在换语言时再调一次）。</summary>
    public static void Build(DataGrid grid, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(text);

        grid.Columns.Clear();

        Add("SheetId", "History.ColSheetId");
        Add("ProjectName", "History.ColProject");
        Add("CreatedOn", "History.ColDate");
        Add("SampleCount", "History.ColSamples");

        void Add(string path, string key) =>
            grid.Columns.Add(
                new DataGridTextColumn
                {
                    Header = text(key),
                    Binding = new Binding(path),
                }
            );
    }
}
