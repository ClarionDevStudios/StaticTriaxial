namespace STS.Method.ShearStrength;

using STS.Control;

/// <summary>
/// 方法参数（骨架占位）：声明为空集 —— 参数配置页仍会显示"框架级通用目录"
/// （<c>MachineParameterCatalog</c>，含通道 / 闸门 / 速率等机器参数）。
/// 周内按现役"全自动土工项目"的参数表（围压 / 反压 / 排水条件 / 速率…）逐项补齐。
/// </summary>
public sealed class ShearStrengthParameters : ITestParameters
{
    public IReadOnlyList<ParameterDefinition> Definitions => [];

    public Task<IReadOnlyDictionary<string, string>> LoadAsync(
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(StringComparer.Ordinal)
        );

    public Task SaveAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;
}
