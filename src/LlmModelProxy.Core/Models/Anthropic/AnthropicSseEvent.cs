using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmModelProxy.Core.Models.Anthropic;

/// <summary>
/// A single Anthropic-format SSE event to be written to the client.
/// </summary>
public sealed class AnthropicSseEvent
{
    public string Event { get; init; } = string.Empty;
    public object Data { get; init; } = new();

    /// <summary>Serialise to wire format: "event: X\ndata: Y\n\n"</summary>
    public string ToWire(JsonSerializerOptions? opts = null) =>
        $"event: {Event}\ndata: {JsonSerializer.Serialize(Data, opts)}\n\n";

    // ── Factory methods ───────────────────────────────────────────────────────

    public static AnthropicSseEvent MessageStart(AnthropicResponse response) => new()
    {
        Event = "message_start",
        Data = new { type = "message_start", message = response }
    };

    public static AnthropicSseEvent ContentBlockStart(int index, AnthropicContentBlock block) => new()
    {
        Event = "content_block_start",
        Data = new { type = "content_block_start", index, content_block = block }
    };

    public static AnthropicSseEvent ContentBlockDelta(int index, AnthropicDelta delta) => new()
    {
        Event = "content_block_delta",
        Data = new { type = "content_block_delta", index, delta }
    };

    public static AnthropicSseEvent ContentBlockStop(int index) => new()
    {
        Event = "content_block_stop",
        Data = new { type = "content_block_stop", index }
    };

    /// <param name="stopReason">The Anthropic stop reason (e.g. "end_turn", "tool_use").</param>
    /// <param name="usage">Optional final usage; pass <c>null</c> to omit.</param>
    public static AnthropicSseEvent MessageDelta(string stopReason, AnthropicUsage? usage) => new()
    {
        Event = "message_delta",
        Data = usage is null
            ? (object)new { type = "message_delta", delta = new { type = "message_delta", stop_reason = stopReason, stop_sequence = (string?)null } }
            : new { type = "message_delta", delta = new { type = "message_delta", stop_reason = stopReason, stop_sequence = (string?)null }, usage }
    };

    public static AnthropicSseEvent MessageStop() => new()
    {
        Event = "message_stop",
        Data = new { type = "message_stop" }
    };

    public static AnthropicSseEvent Ping() => new()
    {
        Event = "ping",
        Data = new { type = "ping" }
    };
}
