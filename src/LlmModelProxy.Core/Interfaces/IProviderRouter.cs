namespace LlmModelProxy.Core.Interfaces;

/// <summary>
/// Selects the correct <see cref="ILlmProvider"/> for a given model string.
/// </summary>
public interface IProviderRouter
{
    /// <summary>
    /// Returns the provider that should handle <paramref name="modelId"/>.
    /// Throws <see cref="InvalidOperationException"/> if no provider matches.
    /// </summary>
    ILlmProvider Route(string modelId);
}
