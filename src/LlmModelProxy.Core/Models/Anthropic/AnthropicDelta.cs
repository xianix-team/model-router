using System.Text.Json.Serialization;

namespace LlmModelProxy.Core.Models.Anthropic;

/// <summary>
/// Represents the inner <c>delta</c> object inside a <c>content_block_delta</c> SSE event.
/// </summary>
public sealed class AnthropicDelta
{
    /// <summary><c>text_delta</c> or <c>input_json_delta</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>Present when <see cref="Type"/> is <c>text_delta</c>.</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>Present when <see cref="Type"/> is <c>input_json_delta</c>.</summary>
    [JsonPropertyName("partial_json")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PartialJson { get; init; }
}
