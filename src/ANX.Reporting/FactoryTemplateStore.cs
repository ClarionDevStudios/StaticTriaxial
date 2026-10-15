namespace ANX.Reporting;

using FlexCel.Core;
using FlexCel.XlsAdapter;

/// <summary>
/// **出厂报表模板的通用管理**：版本戳、换代备份、"按语种确保是本版"。
///
/// 用户 2026-10-15 定了长期口径：**以后的报表都按 T0715 这套方式处理** —— 所以把这一段从
/// 方法里抽出来共用，第二个方法（马歇尔）不用再照记忆抄一遍。
///
/// 约定（T0715 定下来的）：
/// <list type="bullet">
///   <item>模板文件名 `<前缀>.<语种>.xlsx`，放在报表根目录的 `Template\` 下；</item>
///   <item>模板里**藏一枚版本戳**：第 1 行 K 列写 `ANX-TEMPLATE-V<n>`，该列**隐藏**
///         （报表本身只占 A~I）—— 写出厂模板时调 <see cref="ApplyStamp"/>；</item>
///   <item>代码侧版本号 **+1 = 出厂模板换代**（改了结构 / 固定文本 / 样式就要 +1）。
///         导出前比对戳：落后、没有戳、读不出来 → 把旧文件**改名备份**（`.yyyyMMddHHmmss.bak.xlsx`，
///         同目录、不删）再重新写出厂模板；戳一致 → 原样保留，**站点手工改过的模板继续生效**。</item>
/// </list>
///
/// 踩过的坑（2026-10-14）：原先只判"模板文件在不在"，于是改了版式之后装机那份旧模板一直没被换掉，
/// 软件照旧导出旧版式的报表，改动看着"没生效"。
/// </summary>
public sealed class FactoryTemplateStore
{
    /// <summary>版本戳的位置：第 1 行 K 列。</summary>
    public const int StampRow = 1;

    public const int StampColumn = 11;

    private readonly string _templatesRoot;
    private readonly string _filePrefix;
    private readonly int _version;
    private readonly IReadOnlyList<string> _cultures;
    private readonly Action<string, string> _writeFactoryTemplate;

    /// <param name="templatesRoot">模板目录（`Report\Template\`）。</param>
    /// <param name="filePrefix">文件名前缀（如 `Bending`）。</param>
    /// <param name="version">本版出厂模板版本号；改了模板就 +1。</param>
    /// <param name="cultures">要保证的语种（如 `zh-CN` / `en-US`）。</param>
    /// <param name="writeFactoryTemplate">写出厂模板的委托：(目标路径, 语种)。</param>
    public FactoryTemplateStore(
        string templatesRoot,
        string filePrefix,
        int version,
        IReadOnlyList<string> cultures,
        Action<string, string> writeFactoryTemplate
    )
    {
        _templatesRoot = templatesRoot;
        _filePrefix = filePrefix;
        _version = version;
        _cultures = cultures;
        _writeFactoryTemplate = writeFactoryTemplate;
    }

    /// <summary>本版的版本戳文本（写出厂模板时由 <see cref="ApplyStamp"/> 写上）。</summary>
    public string VersionStamp => StampText(_version);

    /// <summary>指定语种的模板路径。</summary>
    public string PathFor(string culture) =>
        Path.Combine(_templatesRoot, $"{_filePrefix}.{culture}.xlsx");

    /// <summary>把配置里的每个语种都确保到本版。</summary>
    public void EnsureAll()
    {
        Directory.CreateDirectory(_templatesRoot);

        foreach (var culture in _cultures)
        {
            Ensure(culture);
        }
    }

    /// <summary>确保指定语种的模板是本版，返回它的路径。</summary>
    public string Ensure(string culture)
    {
        Directory.CreateDirectory(_templatesRoot);

        var path = PathFor(culture);
        if (File.Exists(path) && !IsCurrentVersion(path))
        {
            File.Move(path, BackupPath(path));
        }

        if (!File.Exists(path))
        {
            _writeFactoryTemplate(path, culture);
        }

        return path;
    }

    /// <summary>写出厂模板时调这一句：写上版本戳并隐藏该列。</summary>
    public static void ApplyStamp(XlsFile workbook, int version)
    {
        workbook.SetCellValue(StampRow, StampColumn, StampText(version));
        workbook.SetColHidden(StampColumn, true);
    }

    private static string StampText(int version) => $"ANX-TEMPLATE-V{version}";

    /// <summary>
    /// 磁盘上这份模板是不是本版（读隐藏列 K 的版本戳）。
    /// 读不出来（旧版式没有戳 / 文件损坏 / 根本不是本软件产出的表）一律按"要换代"处理 ——
    /// 换代**先备份原文**，所以这里可以放心判"旧"。
    /// </summary>
    private bool IsCurrentVersion(string path)
    {
        try
        {
            var workbook = new XlsFile();
            workbook.Open(path);

            return workbook.GetCellValue(StampRow, StampColumn) is string stamp
                && string.Equals(stamp, VersionStamp, StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>备份文件名：原名 + 时间戳 + `.bak.xlsx`，与原文件同目录（便于用户找回自己改过的那份）。</summary>
    private static string BackupPath(string path) => $"{path}.{DateTime.Now:yyyyMMddHHmmss}.bak.xlsx";
}
