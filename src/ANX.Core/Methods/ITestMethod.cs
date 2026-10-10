namespace ANX.Core.Methods;

using ANX.Core.Control;
using ANX.Core.Units;

/// <summary>声明式字段的类型。界面据此生成录入控件，存储层据此定列类型。</summary>
public enum MethodFieldKind
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Date = 3,
    Boolean = 4,
    Enum = 5,
}

/// <summary>方法的一个声明式字段。</summary>
public sealed record MethodField(
    string Key,
    string DisplayName,
    MethodFieldKind Kind,
    UnitCode Unit = UnitCode.None,
    bool IsRequired = false,
    int Decimals = 2
);

/// <summary>
/// 试验方法的声明式定义。框架靠它生成界面字段、建表与装配判定器；
/// 方法工程只声明"我有哪些字段、我的判定器是谁"，不写框架代码。
/// </summary>
public interface ITestMethod
{
    /// <summary>方法代号（例：T0715）。</summary>
    string MethodCode { get; }

    /// <summary>方法显示名（例：沥青混合料弯曲试验）。</summary>
    string MethodName { get; }

    /// <summary>依据的规范条款（例：JTG 3410-2025 T0715-2025）。</summary>
    string StandardReference { get; }

    /// <summary>试验单与试件的声明式字段。</summary>
    IReadOnlyList<MethodField> Fields { get; }

    /// <summary>创建判定器。</summary>
    ITestAnalyzer CreateAnalyzer();
}
