namespace LlmModelProxy.Providers.Anthropic;

public sealed class AnthropicOptions
{
    public const string Section = "Proxy:Providers:Anthropic";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string AnthropicVersion { get; set; } = "2023-06-01";
}
