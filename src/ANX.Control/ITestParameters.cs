namespace ANX.Control;

/// <summary>参数分组：界面按组把参数分到两个页面，方法只声明分组。</summary>
public enum ParameterGroup
{
    /// <summary>机器与控制参数 → 参数配置页。</summary>
    Machine = 0,

    /// <summary>试件尺寸类参数 → 试样编辑页。</summary>
    Sample = 1,
}

/// <summary>参数值的编辑类型：界面据此选控件（数字框 / 开关 / 文本框）。</summary>
public enum ParameterKind
{
    /// <summary>数值（可用 <see cref="ParameterDefinition.Minimum"/> / <see cref="ParameterDefinition.Maximum"/> 限定范围）。</summary>
    Number = 0,

    /// <summary>开关，值存 `1`/`0`（界面给复选框）。</summary>
    Switch = 1,

    /// <summary>自由文本。</summary>
    Text = 2,

    /// <summary>从固定选项里选一个（下拉列表；现役用单选组，我们改下拉以省空间）。</summary>
    Choice = 3,
}

/// <summary>下拉列表的一个选项：<paramref name="Value"/> 入库，<paramref name="Label"/> 显示。</summary>
public sealed record ParameterOption(string Value, string Label);

/// <summary>参数的作用时点（界面据此分组与提示"什么时候生效"）。</summary>
public enum ParameterScope
{
    /// <summary>运行期：装配进 TestRunProfile，控制循环 / 阶段机在试验过程中用。</summary>
    Runtime = 0,

    /// <summary>计算期：判定与重算时用（试件尺寸、原点修正…）。</summary>
    Computation = 1,

    /// <summary>采集口径：数据从哪个通道 / 传感器来。</summary>
    Acquisition = 2,

    /// <summary>记录与显示：曲线记录策略、界面抽样。</summary>
    Presentation = 3,
}

/// <summary>一个方法参数的声明（界面据此生成编辑控件，方法只声明"我有哪些参数"）。</summary>
/// <param name="Key">参数键（入库用，发布后不改名）。</param>
/// <param name="DisplayName">显示名。</param>
/// <param name="Unit">单位符号，无量纲留空。</param>
/// <param name="DefaultValue">默认值（库里没有该键时用它）。</param>
/// <param name="Kind">编辑类型（数字 / 开关 / 文本）。</param>
/// <param name="Minimum">数值下限（仅 <see cref="ParameterKind.Number"/> 有意义）。</param>
/// <param name="Maximum">数值上限（仅 <see cref="ParameterKind.Number"/> 有意义）。</param>
/// <param name="Scope">作用时点（界面分组与提示用）。</param>
public sealed record ParameterDefinition(
    string Key,
    string DisplayName,
    string Unit,
    string DefaultValue,
    ParameterGroup Group = ParameterGroup.Machine,
    ParameterKind Kind = ParameterKind.Number,
    double? Minimum = null,
    double? Maximum = null,
    ParameterScope Scope = ParameterScope.Runtime,

    /// <summary>一句话说明（界面显示在名称下方，照现役窗体里每项的注释写：这一项管什么）。</summary>
    string Description = "",

    /// <summary>
    /// **待接线**：现役有这个参数、但本软件还没有对应功能（或还没接线）。
    /// 界面照现役的样子把它列出来，但**置灰 + 标注"待接线"**，不让改 —— 避免出现"改了没反应"的假旋钮。
    /// </summary>
    bool PendingIntegration = false,

    /// <summary><see cref="ParameterKind.Choice"/> 的选项；其它类型为 null。</summary>
    IReadOnlyList<ParameterOption>? Options = null
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
