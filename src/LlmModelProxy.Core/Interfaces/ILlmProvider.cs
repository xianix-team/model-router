using LlmModelProxy.Core.Models.Anthropic;

namespace LlmModelProxy.Core.Interfaces;

/// <summary>
/// Single extensibility seam every backend must implement.
/// Register via <c>IServiceCollection.AddLlmProvider&lt;T&gt;()</c>.
/// </summary>
public interface ILlmProvider
{
    /// <summary>Unique provider name, e.g. "openai", "ollama", "azureopenai".</summary>
    string Name { get; }

    /// <summary>Returns true if this provider can handle the given model string.</summary>
    bool SupportsModel(string modelId);

    /// <summary>Buffered (non-streaming) completion.</summary>
    Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streaming completion. Yields Anthropic SSE events that the API
    /// controller writes verbatim to the client response stream.
    /// </summary>
    IAsyncEnumerable<AnthropicSseEvent> StreamAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken = default);
}
