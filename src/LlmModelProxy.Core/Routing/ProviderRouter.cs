using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Core.Routing;

/// <inheritdoc cref="IProviderRouter"/>
public sealed class ProviderRouter : IProviderRouter
{
    private readonly IReadOnlyList<ILlmProvider> _providers;
    private readonly IReadOnlyList<RoutingRule> _rules;

    public ProviderRouter(
        IEnumerable<ILlmProvider> providers,
        IOptions<ProxyOptions> options)
    {
        _providers = [.. providers];
        _rules = options.Value.ProviderRouting.Rules;
    }

    /// <inheritdoc/>
    public ILlmProvider Route(string modelId)
    {
        // 1. Try config-driven prefix rules in order.
        foreach (var rule in _rules)
        {
            if (rule.ModelPrefix == "*" || modelId.StartsWith(rule.ModelPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var named = _providers.FirstOrDefault(p =>
                    p.Name.Equals(rule.Provider, StringComparison.OrdinalIgnoreCase));
                if (named is not null) return named;
            }
        }

        // 2. Fall back to any provider that self-reports support.
        var capable = _providers.FirstOrDefault(p => p.SupportsModel(modelId));
        if (capable is not null) return capable;

        // 3. Final fallback: first registered provider (e.g. openai).
        if (_providers.Count > 0) return _providers[0];

        throw new InvalidOperationException(
            $"No provider found for model '{modelId}'. Register at least one ILlmProvider.");
    }
}
