namespace ANX.Control;

using System.Globalization;

/// <summary>参数值的校验结果（界面据此出本地化文案）。</summary>
public enum ParameterValidationError
{
    /// <summary>合法。</summary>
    None = 0,

    /// <summary>空值。</summary>
    Required = 1,

    /// <summary>不是数字。</summary>
    NotANumber = 2,

    /// <summary>超出声明范围。</summary>
    OutOfRange = 3,

    /// <summary>开关值不是 0 / 1。</summary>
    InvalidSwitch = 4,
}

/// <summary>
/// 参数值校验：按 <see cref="ParameterDefinition"/> 声明的**类型与范围**判合法性。
/// 纯逻辑、无界面依赖 —— 界面把结果翻成文案，保存前也用它做闸门。
/// </summary>
public static class ParameterValidator
{
    /// <summary>校验一个参数值；<paramref name="number"/> 回传解析出的数值（非数值类为 0）。</summary>
    public static ParameterValidationError Validate(
        ParameterDefinition definition,
        string? text,
        out double number
    )
    {
        ArgumentNullException.ThrowIfNull(definition);

        number = 0;
        var value = text?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            // 声明里写了"允许留空"的项（可选的文本字段）空着就是合法值
            return definition.AllowEmpty ? ParameterValidationError.None : ParameterValidationError.Required;
        }

        if (definition.Kind == ParameterKind.Text)
        {
            return ParameterValidationError.None;
        }

        if (definition.Kind == ParameterKind.Switch)
        {
            if (value is "0" or "1")
            {
                number = value[0] - '0';
                return ParameterValidationError.None;
            }

            if (bool.TryParse(value, out var flag))
            {
                number = flag ? 1 : 0;
                return ParameterValidationError.None;
            }

            return ParameterValidationError.InvalidSwitch;
        }

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return ParameterValidationError.NotANumber;
        }

        if (
            (definition.Minimum is { } minimum && number < minimum)
            || (definition.Maximum is { } maximum && number > maximum)
        )
        {
            return ParameterValidationError.OutOfRange;
        }

        return ParameterValidationError.None;
    }

    /// <summary>
    /// 数值参数的默认值是否在声明范围内（方法声明自检用；声明写错时早暴露）。
    /// 非数值类或未声明范围的参数一律返回 true。
    /// </summary>
    public static bool IsDefaultValueUsable(ParameterDefinition definition)
    {
        if (definition.Kind != ParameterKind.Number)
        {
            return true;
        }

        var error = Validate(definition, definition.DefaultValue, out _);
        return error == ParameterValidationError.None;
    }
}
