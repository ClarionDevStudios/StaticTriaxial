namespace ANX.Core.Methods;

/// <summary>
/// 列的数据类型。只留这三种：数值列一律用真实类型，不用 TEXT 冒充数字
/// （现役用 `VARCHAR(10)` 存数值，读取时靠 `NumField` 兜空串，这条路不走）。
/// </summary>
public enum ColumnType
{
    /// <summary>整数（SQLite INTEGER）。</summary>
    Integer = 0,

    /// <summary>实数（SQLite REAL）。</summary>
    Real = 1,

    /// <summary>文本（SQLite TEXT；曲线 JSON、单号、项目名等）。</summary>
    Text = 2,
}

/// <summary>
/// 一列的定义。<paramref name="Comment"/> 是这一列的中文含义说明（含单位、取值口径），
/// 由 <c>SchemaBuilder</c> 写进生成的建表 SQL 注释里 —— 目的就是让人能直接读 SQL 审核字段。
/// </summary>
public sealed record ColumnDefinition(
    string Name,
    ColumnType Type,
    bool IsRequired = false,
    string? Comment = null
);

/// <summary>
/// 一张表的定义。框架只认这些结构信息，不认任何具体方法字段的含义。
///
/// 主键是**列名列表**：单列（主表/模板表）或复合键（明细表 = <c>(SheetID, SampleID)</c>，
/// 与现役软件一致，用户 2026-10-13）。
/// </summary>
public sealed record TableDefinition(
    string TableName,
    IReadOnlyList<string> PrimaryKeyColumns,
    IReadOnlyList<ColumnDefinition> Columns,
    string? Comment = null
)
{
    /// <summary>
    /// 单列主键的列名（复合键时取第一列）。只给"按主键定位一行"的单列场景用，
    /// 例如按单号读写主表；明细表请用两个条件定位。
    /// </summary>
    public string PrimaryKeyColumn => PrimaryKeyColumns[0];
}

/// <summary>
/// 方法持久化模块：方法声明自己的三张表（Main / Detail / Template），框架据此建表。
/// 这样 ANX.Storage 里不会出现任何具体方法的字段名，方法工程也不必依赖存储实现。
/// </summary>
public interface IMethodPersistenceModule
{
    /// <summary>方法代号，与 <see cref="ITestMethod.MethodCode"/> 一致。</summary>
    string MethodCode { get; }

    /// <summary>试验单主表（一试单一行的头信息 + 组统计列）。</summary>
    TableDefinition MainTable { get; }

    /// <summary>试件明细表（一试件一行，含曲线 JSON 与单件判定值）。</summary>
    TableDefinition DetailTable { get; }

    /// <summary>参数模板表（去掉组统计列的主表，用于参数预设与继承上次信息）。</summary>
    TableDefinition TemplateTable { get; }
}
