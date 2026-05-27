using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmModelProxy.Core.Models.Anthropic;

public sealed class AnthropicTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; set; }

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; }
}

public sealed class AnthropicToolChoice
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "auto"; // auto | none | tool | any

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
