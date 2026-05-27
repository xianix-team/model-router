namespace LlmModelProxy.Core.Routing;

/// <summary>
/// Maps a model-string prefix to a named provider.
/// Use "*" as a catch-all fallback.
/// </summary>
public sealed class RoutingRule
{
    /// <summary>Model prefix to match, e.g. "ollama/", "claude-", "*".</summary>
    public string ModelPrefix { get; set; } = string.Empty;

    /// <summary>Provider name, e.g. "openai", "ollama", "azureopenai".</summary>
    public string Provider { get; set; } = string.Empty;
}
