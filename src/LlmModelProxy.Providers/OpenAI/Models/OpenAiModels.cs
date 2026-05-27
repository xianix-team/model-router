using System.Text.Json;
using System.Text.Json.Serialization;

namespace LlmModelProxy.Providers.OpenAI.Models;

// ── Request ──────────────────────────────────────────────────────────────────

public sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
    [JsonPropertyName("messages")] public List<OpenAiMessage> Messages { get; set; } = [];
    [JsonPropertyName("tools")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiTool>? Tools { get; set; }
    [JsonPropertyName("tool_choice")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolChoice { get; set; }
    [JsonPropertyName("stream")] public bool Stream { get; set; }
    [JsonPropertyName("parallel_tool_calls")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ParallelToolCalls { get; set; }
    [JsonPropertyName("max_tokens")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }
    [JsonPropertyName("max_completion_tokens")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCompletionTokens { get; set; }
    [JsonPropertyName("temperature")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Temperature { get; set; }
    [JsonPropertyName("top_p")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? TopP { get; set; }
    [JsonPropertyName("stop")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stop { get; set; }
    [JsonPropertyName("stream_options")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiStreamOptions? StreamOptions { get; set; }
}

public sealed class OpenAiStreamOptions
{
    [JsonPropertyName("include_usage")] public bool IncludeUsage { get; set; } = true;
}

public sealed class OpenAiMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("content")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; set; }
    [JsonPropertyName("tool_calls")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
    [JsonPropertyName("tool_call_id")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
}

public sealed class OpenAiTool
{
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiFunction Function { get; set; } = new();
}

public sealed class OpenAiFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    [JsonPropertyName("parameters")] public JsonElement Parameters { get; set; }
    [JsonPropertyName("strict")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Strict { get; set; }
}

public sealed class OpenAiToolCall
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = "function";
    [JsonPropertyName("function")] public OpenAiToolCallFunction Function { get; set; } = new();
}

public sealed class OpenAiToolCallFunction
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = string.Empty;
}

// ── Non-streaming response ────────────────────────────────────────────────────

public sealed class OpenAiChatResponse
{
    [JsonPropertyName("choices")] public List<OpenAiChoice> Choices { get; set; } = [];
    [JsonPropertyName("usage")] public OpenAiUsage? Usage { get; set; }
}

public sealed class OpenAiChoice
{
    [JsonPropertyName("message")] public OpenAiMessage? Message { get; set; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}

public sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
}

// ── Streaming chunk ───────────────────────────────────────────────────────────

public sealed class OpenAiStreamChunk
{
    [JsonPropertyName("choices")] public List<OpenAiStreamChoice> Choices { get; set; } = [];
    [JsonPropertyName("usage")] public OpenAiUsage? Usage { get; set; }
}

public sealed class OpenAiStreamChoice
{
    [JsonPropertyName("delta")] public OpenAiDelta Delta { get; set; } = new();
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; set; }
}

public sealed class OpenAiDelta
{
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("tool_calls")] public List<OpenAiDeltaToolCall>? ToolCalls { get; set; }
}

public sealed class OpenAiDeltaToolCall
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("function")] public OpenAiDeltaFunction? Function { get; set; }
}

public sealed class OpenAiDeltaFunction
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("arguments")] public string? Arguments { get; set; }
}
