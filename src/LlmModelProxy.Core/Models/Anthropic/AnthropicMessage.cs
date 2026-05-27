using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmModelProxy.Core.Models.Anthropic;

public sealed class AnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Either a plain string or an array of content blocks
    /// (text / tool_use / tool_result / image).
    /// </summary>
    [JsonPropertyName("content")]
    public JsonElement Content { get; set; }

    /// <summary>Returns all text parts joined, for snippet logging.</summary>
    public string TextSnippet(int maxChars = 80)
    {
        if (Content.ValueKind == JsonValueKind.String)
        {
            var s = Content.GetString() ?? string.Empty;
            return s.Length <= maxChars ? s : s[..maxChars] + "...";
        }
        if (Content.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var el in Content.EnumerateArray())
            {
                if (el.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                    el.TryGetProperty("text", out var tx))
                    sb.Append(tx.GetString());
            }
            var full = sb.ToString();
            return full.Length <= maxChars ? full : full[..maxChars] + "...";
        }
        return string.Empty;
    }
}
