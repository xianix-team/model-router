namespace LlmModelProxy.Providers.OpenAI;

public sealed class OpenAiOptions
{
    public const string Section = "Proxy:Providers:OpenAI";

    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.openai.com";
    public string DefaultModel { get; set; } = "gpt-4o";
    public int MaxCompletionTokensCap { get; set; } = 16_384;
    public bool SkipCompletionTokenCap { get; set; }
}
