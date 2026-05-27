using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmModelProxy.Core.Models.Anthropic;

/// <summary>
/// Anthropic /v1/messages request body received from Claude CLI.
/// </summary>
public sealed class AnthropicRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<AnthropicMessage> Messages { get; set; } = [];

    /// <summary>
    /// May be a plain string or an array of { type:"text", text:"..." } blocks.
    /// Use <see cref="FlattenSystem"/> to normalise.
    /// </summary>
    [JsonPropertyName("system")]
    public JsonElement? System { get; set; }

    [JsonPropertyName("tools")]
    public List<AnthropicTool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    public AnthropicToolChoice? ToolChoice { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("top_p")]
    public float? TopP { get; set; }

    [JsonPropertyName("stop_sequences")]
    public List<string>? StopSequences { get; set; }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Flatten the polymorphic system field to a plain string.</summary>
    public string FlattenSystem()
    {
        if (System is null) return string.Empty;
        if (System.Value.ValueKind == JsonValueKind.String)
            return System.Value.GetString() ?? string.Empty;
        if (System.Value.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var el in System.Value.EnumerateArray())
            {
                if (el.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "text" &&
                    el.TryGetProperty("text", out var textEl))
                    parts.Add(textEl.GetString() ?? string.Empty);
            }
            return string.Join("\n", parts).Trim();
        }
        return string.Empty;
    }
}
