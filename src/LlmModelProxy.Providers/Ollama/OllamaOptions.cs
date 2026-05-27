namespace LlmModelProxy.Providers.Ollama;

public sealed class OllamaOptions
{
    public const string Section = "Proxy:Providers:Ollama";

    /// <summary>Ollama server base URL, e.g. http://ollama:11434</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Default Ollama model tag, e.g. "llama3:8b".
    /// Used when no model is matched by routing rules.
    /// </summary>
    public string DefaultModel { get; set; } = "llama3:8b";

    /// <summary>
    /// Model name prefixes that this provider accepts (comma-separated), e.g. "llama,mistral,phi".
    /// </summary>
    public string ModelPrefixes { get; set; } = "llama,mistral,phi,gemma,qwen";

    public int MaxCompletionTokensCap { get; set; } = 32_768;
    public bool SkipCompletionTokenCap { get; set; } = true;
}
