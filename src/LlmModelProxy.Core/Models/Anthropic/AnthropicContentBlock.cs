using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmModelProxy.Core.Models.Anthropic;

public sealed class AnthropicContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // text | tool_use

    // ── text block ────────────────────────────────────────────────────────────
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    // ── tool_use block ────────────────────────────────────────────────────────
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }

    // ── factories ─────────────────────────────────────────────────────────────
    public static AnthropicContentBlock TextBlock(string text) => new() { Type = "text", Text = text };

    public static AnthropicContentBlock ToolUseBlock(string id, string name, JsonElement input) =>
        new() { Type = "tool_use", Id = id, Name = name, Input = input };
}
