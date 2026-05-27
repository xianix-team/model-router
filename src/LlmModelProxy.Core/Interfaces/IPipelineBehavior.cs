using LlmModelProxy.Core.Models.Anthropic;

namespace LlmModelProxy.Core.Interfaces;

/// <summary>
/// Ordered middleware that can inspect or mutate the request before
/// it reaches the provider. Implementations are registered with DI
/// and composed by <see cref="Pipeline.ProxyPipeline"/>.
/// </summary>
public interface IPipelineBehavior
{
    /// <summary>Execution order — lower runs first.</summary>
    int Order { get; }

    /// <summary>Mutate or inspect the request in-place.</summary>
    Task ExecuteAsync(AnthropicRequest request, CancellationToken cancellationToken = default);
}
