using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;

namespace LlmModelProxy.Core.Pipeline;

/// <summary>
/// Runs all registered <see cref="IPipelineBehavior"/> instances in order
/// before dispatching to the provider.
/// </summary>
public sealed class ProxyPipeline
{
    private readonly IReadOnlyList<IPipelineBehavior> _behaviors;

    public ProxyPipeline(IEnumerable<IPipelineBehavior> behaviors)
    {
        _behaviors = [.. behaviors.OrderBy(b => b.Order)];
    }

    public async Task ExecuteAsync(AnthropicRequest request, CancellationToken ct = default)
    {
        foreach (var behavior in _behaviors)
            await behavior.ExecuteAsync(request, ct);
    }
}
