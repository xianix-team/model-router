using System.Text.Json.Serialization;

namespace LlmModelProxy.Core.Models.Anthropic;

public sealed class AnthropicResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"msg_{Guid.NewGuid():N}";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "assistant";

    [JsonPropertyName("content")]
    public List<AnthropicContentBlock> Content { get; set; } = [];

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; set; }

    [JsonPropertyName("usage")]
    public AnthropicUsage Usage { get; set; } = new();
}

public sealed class AnthropicUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }
}
