namespace ANX.Control;

/// <summary>参数分组：界面按组把参数分到两个页面，方法只声明分组。</summary>
public enum ParameterGroup
{
    /// <summary>机器与控制参数 → 参数配置页。</summary>
    Machine = 0,

    /// <summary>试件尺寸类参数 → 试样编辑页。</summary>
    Sample = 1,
}

/// <summary>一个方法参数的声明（界面据此生成编辑控件，方法只声明"我有哪些参数"）。</summary>
/// <param name="Key">参数键（入库用，发布后不改名）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Unit">单位符号，无量纲留空。</param>
/// <param name="DefaultValue">默认值（库里没有该键时用它）。</param>
public sealed record ParameterDefinition(
    string Key,
    string DisplayName,
    string Unit,
    string DefaultValue,
    ParameterGroup Group = ParameterGroup.Machine
);

/// <summary>
/// 方法参数的读写契约。两类页面共用它：**试样编辑页**（试件尺寸）与**参数配置页**（机器与控制参数）。
/// </summary>
public interface ITestParameters
{
    /// <summary>本方法声明的全部参数。</summary>
    IReadOnlyList<ParameterDefinition> Definitions { get; }

    /// <summary>读当前值（库里没有的键回落到默认值）。</summary>
    Task<IReadOnlyDictionary<string, string>> LoadAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>保存若干参数。</summary>
    Task SaveAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default
    );
}
