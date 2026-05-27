namespace LlmModelProxy.Providers.AzureOpenAI;

public sealed class AzureOpenAiOptions
{
    public const string Section = "Proxy:Providers:AzureOpenAI";

    /// <summary>Your Azure OpenAI resource endpoint, e.g. https://my-resource.openai.azure.com</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Azure OpenAI API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Deployment name (maps to the model in OpenAI terms).</summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>API version, e.g. 2024-02-01</summary>
    public string ApiVersion { get; set; } = "2024-02-01";

    public int MaxCompletionTokensCap { get; set; } = 16_384;
    public bool SkipCompletionTokenCap { get; set; }
}
