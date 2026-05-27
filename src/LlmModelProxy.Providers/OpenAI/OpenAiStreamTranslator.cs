using System.Text;
using System.Text.Json;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Core.Pipeline.Behaviors;
using LlmModelProxy.Providers.OpenAI.Models;

namespace LlmModelProxy.Providers.OpenAI;

/// <summary>
/// Converts a sequence of <see cref="OpenAiStreamChunk"/> objects into an
/// <see cref="IAsyncEnumerable{T}"/> of <see cref="AnthropicSseEvent"/>s.
/// Port of <c>openAiStreamChunkToAnthropicEvents()</c> from translator.mjs.
/// </summary>
public static class OpenAiStreamTranslator
{
    // ── Public entry point ────────────────────────────────────────────────────

    public static async IAsyncEnumerable<AnthropicSseEvent> TranslateAsync(
        IAsyncEnumerable<OpenAiStreamChunk> chunks,
        string requestedModelId,
        string anthropicMessageId)
    {
        var state = new StreamState();

        // message_start
        yield return AnthropicSseEvent.MessageStart(new AnthropicResponse
        {
            Id = anthropicMessageId,
            Model = requestedModelId,
            StopReason = null,
            Usage = new AnthropicUsage { InputTokens = 0, OutputTokens = 0 }
        });

        await foreach (var chunk in chunks)
        {
            foreach (var evt in ProcessChunk(chunk, state, requestedModelId))
                yield return evt;
        }

        // If stream ended without an explicit finish_reason, close gracefully.
        if (!state.Done)
        {
            foreach (var evt in FinishStream(state, "end_turn"))
                yield return evt;
        }
    }

    // ── Chunk processing ──────────────────────────────────────────────────────

    private static IEnumerable<AnthropicSseEvent> ProcessChunk(
        OpenAiStreamChunk chunk, StreamState state, string requestedModelId)
    {
        // Usage-only chunk (no choices) — emit message_delta with final usage.
        if (chunk.Choices.Count == 0 && chunk.Usage is not null)
        {
            yield return AnthropicSseEvent.MessageDelta("end_turn", new AnthropicUsage
            {
                InputTokens = chunk.Usage.PromptTokens,
                OutputTokens = chunk.Usage.CompletionTokens
            });
            yield break;
        }

        var choice = chunk.Choices.FirstOrDefault();
        if (choice is null) yield break;

        var delta = choice.Delta;

        // ── Text delta ────────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(delta.Content))
        {
            if (!state.Started)
            {
                state.Started = true;
                state.TextIdx = state.NextIdx++;
                yield return AnthropicSseEvent.ContentBlockStart(state.TextIdx,
                    AnthropicContentBlock.TextBlock(string.Empty));
            }
            yield return AnthropicSseEvent.ContentBlockDelta(state.TextIdx,
                new AnthropicDelta { Type = "text_delta", Text = delta.Content });
        }

        // ── Tool call deltas ──────────────────────────────────────────────────
        if (delta.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in delta.ToolCalls)
            {
                foreach (var evt in ProcessToolCallDelta(tc, state))
                    yield return evt;
            }
        }

        // ── finish_reason ─────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(choice.FinishReason))
        {
            var mapped = MapStopReason(choice.FinishReason);
            foreach (var evt in FinishStream(state, mapped))
                yield return evt;
        }
    }

    private static IEnumerable<AnthropicSseEvent> ProcessToolCallDelta(
        OpenAiDeltaToolCall tc, StreamState state)
    {
        if (!state.ToolByIndex.TryGetValue(tc.Index, out var slot))
        {
            // First delta for this slot: open a new tool_use block.
            slot = new ToolCallSlot
            {
                Index = tc.Index,
                Id = tc.Id ?? $"call_{Guid.NewGuid():N}",
                Name = tc.Function?.Name ?? string.Empty,
                ArgsBuffer = new StringBuilder(),
                IsDone = false
            };
            state.ToolByIndex[tc.Index] = slot;

            // Check if this is the synthetic __task_complete__ tool.
            if (slot.Name == SyntheticToolInjectionBehavior.SyntheticToolName)
            {
                slot.IsDone = true;
                // Don't open a content block — buffer args instead.
            }
            else
            {
                // Close any open text block first.
                if (state.TextIdx >= 0)
                {
                    yield return AnthropicSseEvent.ContentBlockStop(state.TextIdx);
                    state.TextIdx = -1;
                }

                slot.BlockIdx = state.NextIdx++;
                yield return AnthropicSseEvent.ContentBlockStart(slot.BlockIdx,
                    AnthropicContentBlock.ToolUseBlock(slot.Id, slot.Name,
                        JsonSerializer.SerializeToElement(new { })));
            }
        }

        // Accumulate arguments.
        if (!string.IsNullOrEmpty(tc.Function?.Arguments))
            slot.ArgsBuffer.Append(tc.Function.Arguments);

        // Emit args delta (only for real tool blocks, not the synthetic done tool).
        if (!slot.IsDone && !string.IsNullOrEmpty(tc.Function?.Arguments))
        {
            yield return AnthropicSseEvent.ContentBlockDelta(slot.BlockIdx,
                new AnthropicDelta { Type = "input_json_delta", PartialJson = tc.Function.Arguments });
        }
    }

    private static IEnumerable<AnthropicSseEvent> FinishStream(StreamState state, string stopReason)
    {
        if (state.Done) yield break;
        state.Done = true;

        // Close open text block.
        if (state.TextIdx >= 0)
        {
            yield return AnthropicSseEvent.ContentBlockStop(state.TextIdx);
            state.TextIdx = -1;
        }

        // Intercept __task_complete__ — emit its result as a text block.
        var doneSlot = state.ToolByIndex.Values.FirstOrDefault(s => s.IsDone);
        if (doneSlot is not null)
        {
            var rawArgs = doneSlot.ArgsBuffer.ToString();
            var resultText = ExtractDoneResult(rawArgs);

            var textIdx = state.NextIdx++;
            yield return AnthropicSseEvent.ContentBlockStart(textIdx,
                AnthropicContentBlock.TextBlock(string.Empty));
            if (!string.IsNullOrEmpty(resultText))
                yield return AnthropicSseEvent.ContentBlockDelta(textIdx,
                    new AnthropicDelta { Type = "text_delta", Text = resultText });
            yield return AnthropicSseEvent.ContentBlockStop(textIdx);

            yield return AnthropicSseEvent.MessageDelta("end_turn", null);
            yield return AnthropicSseEvent.MessageStop();
            yield break;
        }

        // Close real tool blocks.
        foreach (var slot in state.ToolByIndex.Values.Where(s => !s.IsDone))
            yield return AnthropicSseEvent.ContentBlockStop(slot.BlockIdx);

        // If nothing was ever opened, emit an empty text block.
        if (!state.Started && state.ToolByIndex.Count == 0)
        {
            var idx = state.NextIdx++;
            yield return AnthropicSseEvent.ContentBlockStart(idx,
                AnthropicContentBlock.TextBlock(string.Empty));
            yield return AnthropicSseEvent.ContentBlockStop(idx);
        }

        yield return AnthropicSseEvent.MessageDelta(stopReason, null);
        yield return AnthropicSseEvent.MessageStop();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ExtractDoneResult(string rawArgs)
    {
        if (string.IsNullOrWhiteSpace(rawArgs)) return string.Empty;
        try
        {
            var doc = JsonDocument.Parse(rawArgs);
            if (doc.RootElement.TryGetProperty("result", out var r) &&
                r.ValueKind == JsonValueKind.String)
                return r.GetString() ?? string.Empty;
        }
        catch { /* fall through */ }
        return rawArgs;
    }

    internal static string MapStopReason(string? openAiReason) => openAiReason switch
    {
        "tool_calls" or "function_call" => "tool_use",
        "length" => "max_tokens",
        _ => "end_turn"
    };

    // ── State ─────────────────────────────────────────────────────────────────

    private sealed class StreamState
    {
        public bool Started { get; set; }
        public bool Done { get; set; }
        public int NextIdx { get; set; }
        public int TextIdx { get; set; } = -1;
        public Dictionary<int, ToolCallSlot> ToolByIndex { get; } = new();
    }

    private sealed class ToolCallSlot
    {
        public int Index { get; init; }
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public StringBuilder ArgsBuffer { get; init; } = new();
        public bool IsDone { get; set; }
        public int BlockIdx { get; set; }
    }
}
