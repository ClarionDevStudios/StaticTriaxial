namespace ANX.Control.Tests;

using ANX.Control;

/// <summary>
/// 参数值校验（参数配置页的闸门）：按声明类型与范围判 —— 数字 / 开关 / 文本、必填、越界。
/// 纯逻辑，所以能在这里完全锁住；界面只负责把错误枚举翻成文案。
/// </summary>
public sealed class ParameterValidatorTests
{
    private static ParameterDefinition Number(
        string defaultValue,
        double? minimum = null,
        double? maximum = null
    ) =>
        new("K", "键", "mm", defaultValue, ParameterGroup.Machine, ParameterKind.Number, minimum, maximum);

    [Fact]
    public void 数字参数应校验可解析与范围()
    {
        var definition = Number("50", 0.1, 50);

        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(definition, "50", out var value));
        Assert.Equal(50.0, value, 6);

        Assert.Equal(ParameterValidationError.NotANumber, ParameterValidator.Validate(definition, "abc", out _));
        Assert.Equal(ParameterValidationError.OutOfRange, ParameterValidator.Validate(definition, "60", out _));
        Assert.Equal(ParameterValidationError.OutOfRange, ParameterValidator.Validate(definition, "0", out _));
        Assert.Equal(ParameterValidationError.Required, ParameterValidator.Validate(definition, "  ", out _));

        // 边界值算合法（0.1 / 50 都收）
        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(definition, "0.1", out _));
        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(definition, "50", out _));
    }

    [Fact]
    public void 开关参数只认零和一()
    {
        var definition = new ParameterDefinition(
            "S", "开关", "", "1", ParameterGroup.Machine, ParameterKind.Switch
        );

        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(definition, "1", out var on));
        Assert.Equal(1.0, on, 6);
        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(definition, "0", out var off));
        Assert.Equal(0.0, off, 6);

        // 手输 true/false 也认（界面给的是复选框，这只是容错）
        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(definition, "true", out _));

        Assert.Equal(ParameterValidationError.InvalidSwitch, ParameterValidator.Validate(definition, "2", out _));
        Assert.Equal(ParameterValidationError.Required, ParameterValidator.Validate(definition, "", out _));
    }

    [Fact]
    public void 文本参数只要求非空()
    {
        var definition = new ParameterDefinition(
            "T", "文本", "", "abc", ParameterGroup.Machine, ParameterKind.Text
        );

        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(definition, "任何内容", out _));
        Assert.Equal(ParameterValidationError.Required, ParameterValidator.Validate(definition, " ", out _));
    }

    /// <summary>
    /// **允许留空**的项（用户 2026-10-16：试样信息「试样描述」这种可选文本）空着就是合法值 ——
    /// 不写 AllowEmpty 的项照旧按"必填没填"挡住保存。
    /// </summary>
    [Fact]
    public void 声明允许留空的项空值应合法()
    {
        var optional = new ParameterDefinition(
            "Desc",
            "试样描述",
            string.Empty,
            string.Empty,
            ParameterGroup.Sample,
            ParameterKind.Text,
            AllowEmpty: true
        );
        var required = optional with { Key = "Name", AllowEmpty = false };

        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(optional, "", out _));
        Assert.Equal(ParameterValidationError.None, ParameterValidator.Validate(optional, "  ", out _));
        Assert.Equal(ParameterValidationError.Required, ParameterValidator.Validate(required, "", out _));
    }

    [Fact]
    public void 默认值应落在声明范围内()
    {
        Assert.True(ParameterValidator.IsDefaultValueUsable(Number("50", 0.1, 50)));
        Assert.False(ParameterValidator.IsDefaultValueUsable(Number("60", 0.1, 50)));

        // 未声明范围的数字参数不判
        Assert.True(ParameterValidator.IsDefaultValueUsable(Number("9999")));
    }
}
