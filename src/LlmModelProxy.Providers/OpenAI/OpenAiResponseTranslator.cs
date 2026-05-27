using System.Text.Json;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Core.Pipeline.Behaviors;
using LlmModelProxy.Providers.OpenAI.Models;

namespace LlmModelProxy.Providers.OpenAI;

/// <summary>
/// Translates a buffered <see cref="OpenAiChatResponse"/> to an
/// <see cref="AnthropicResponse"/>.  Intercepts <c>__task_complete__</c>
/// calls and converts them to plain text end_turn responses.
/// Port of <c>openAICompletionToAnthropic()</c> from translator.mjs.
/// </summary>
public static class OpenAiResponseTranslator
{
    public static AnthropicResponse Translate(OpenAiChatResponse response, string requestedModelId)
    {
        var choice = response.Choices.FirstOrDefault();
        var msg = choice?.Message;

        // Intercept __task_complete__ synthetic tool call.
        var doneResult = ExtractTaskCompleteResult(msg?.ToolCalls);
        if (doneResult is not null)
        {
            return new AnthropicResponse
            {
                Model = requestedModelId,
                Content = [AnthropicContentBlock.TextBlock(doneResult)],
                StopReason = "end_turn",
                Usage = MapUsage(response.Usage)
            };
        }

        var content = new List<AnthropicContentBlock>();

        if (!string.IsNullOrEmpty(msg?.Content))
            content.Add(AnthropicContentBlock.TextBlock(msg.Content!));

        if (msg?.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in msg.ToolCalls)
            {
                var input = SafeParseJsonObject(tc.Function.Arguments);
                content.Add(AnthropicContentBlock.ToolUseBlock(tc.Id, tc.Function.Name, input));
            }
        }

        if (content.Count == 0)
            content.Add(AnthropicContentBlock.TextBlock(string.Empty));

        return new AnthropicResponse
        {
            Model = requestedModelId,
            Content = content,
            StopReason = MapStopReason(choice?.FinishReason),
            Usage = MapUsage(response.Usage)
        };
    }

    public static string? ExtractTaskCompleteResult(List<OpenAiToolCall>? toolCalls)
    {
        if (toolCalls is null || toolCalls.Count == 0) return null;
        var first = toolCalls[0];
        if (first.Function.Name != SyntheticToolInjectionBehavior.SyntheticToolName) return null;
        var parsed = SafeParseJsonObject(first.Function.Arguments);
        if (parsed.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String)
            return r.GetString();
        return first.Function.Arguments;
    }

    public static string MapStopReason(string? openAiReason) => openAiReason switch
    {
        "tool_calls" or "function_call" => "tool_use",
        "length" => "max_tokens",
        _ => "end_turn"
    };

    private static AnthropicUsage MapUsage(OpenAiUsage? u) => new()
    {
        InputTokens = u?.PromptTokens ?? 0,
        OutputTokens = u?.CompletionTokens ?? 0
    };

    public static JsonElement SafeParseJsonObject(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return JsonSerializer.SerializeToElement(new { });
        try
        {
            var doc = JsonDocument.Parse(s);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.Clone()
                : JsonSerializer.SerializeToElement(new { _raw = s });
        }
        catch
        {
            return JsonSerializer.SerializeToElement(new { _raw_arguments = s });
        }
    }
}
