using System.Text.Json;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Core.Pipeline.Behaviors;
using LlmModelProxy.Providers.OpenAI.Models;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Providers.OpenAI;

/// <summary>
/// Translates an <see cref="AnthropicRequest"/> (already processed by the pipeline)
/// into an <see cref="OpenAiChatRequest"/>.
/// Faithful C# port of <c>anthropicMessagesToOpenAIBody()</c> from translator.mjs.
/// </summary>
public sealed class OpenAiRequestTranslator
{
    /// <summary>
    /// Claude CLI meta-tools that are never forwarded to OpenAI regardless of context.
    /// </summary>
    private static readonly HashSet<string> MetaToolBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "RemoteTrigger",
        "AgentUsage",
    };

    /// <summary>
    /// Returns <c>true</c> when the request contains a Claude CLI slash-command
    /// invocation marker (<c>&lt;command-name&gt;</c> tag).  We use this to decide
    /// whether to expose the <c>Skill</c> tool to the upstream model.
    ///
    /// <list type="bullet">
    ///   <item>No tag present → <c>Skill</c> is stripped. The model cannot call it,
    ///   so it responds with text or real tools.  Fixes the "hi → Skill(superpowers)"
    ///   false-positive.</item>
    ///   <item>Tag present (e.g. <c>/doc-agent:generate-docs</c>) → <c>Skill</c>
    ///   remains in the tool list so the model dispatches the command correctly.</item>
    /// </list>
    ///
    /// This is a structural guard at the proxy layer — no model instruction required.
    /// </summary>
    private static bool HasSlashCommandTag(AnthropicRequest req)
    {
        const string tag = "<command-name>";

        var sys = req.FlattenSystem();
        if (sys.Contains(tag, StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var msg in req.Messages)
        {
            var raw = msg.Content.ValueKind == JsonValueKind.String
                ? (msg.Content.GetString() ?? string.Empty)
                : msg.Content.GetRawText();
            if (raw.Contains(tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private readonly OpenAiOptions _openAiOpts;
    private readonly PipelineOptions _pipelineOpts;

    public OpenAiRequestTranslator(
        IOptions<OpenAiOptions> openAiOpts,
        IOptions<PipelineOptions> pipelineOpts)
    {
        _openAiOpts = openAiOpts.Value;
        _pipelineOpts = pipelineOpts.Value;
    }

    public OpenAiChatRequest Translate(AnthropicRequest req)
    {
        var messages = new List<OpenAiMessage>();

        // System prompt (already merged by the pipeline behavior).
        var sysText = req.FlattenSystem();
        if (!string.IsNullOrWhiteSpace(sysText))
            messages.Add(new OpenAiMessage { Role = "system", Content = sysText });

        // Conversation history.
        foreach (var msg in req.Messages)
        {
            if (msg.Role == "user")
                messages.AddRange(TranslateUserTurn(msg.Content));
            else if (msg.Role == "assistant")
                messages.Add(TranslateAssistantTurn(msg.Content));
            else
                messages.Add(new OpenAiMessage
                {
                    Role = "user",
                    Content = $"[{msg.Role}]\n{msg.Content}"
                });
        }

        var out_ = new OpenAiChatRequest
        {
            Model = _openAiOpts.DefaultModel,
            Messages = messages,
            Stream = req.Stream,
            StreamOptions = req.Stream ? new OpenAiStreamOptions { IncludeUsage = true } : null,
            Temperature = req.Temperature,
            TopP = req.TopP,
            Stop = req.StopSequences
        };

        // Token cap.
        var cap = _openAiOpts.SkipCompletionTokenCap
            ? int.MaxValue
            : _openAiOpts.MaxCompletionTokensCap;

        if (req.MaxCompletionTokens.HasValue)
            out_.MaxCompletionTokens = Clamp(req.MaxCompletionTokens.Value, cap);
        else if (req.MaxTokens.HasValue)
            out_.MaxTokens = Clamp(req.MaxTokens.Value, cap);

        // Tools — strip Claude CLI meta-tools before forwarding to OpenAI.
        // Skill is only exposed when the request contains a <command-name> tag
        // (i.e. the user explicitly invoked a slash command). Without that tag
        // the model would spontaneously call Skill("superpowers") for "hi".
        bool hasSlashCommand = HasSlashCommandTag(req);
        var forwardableTools = req.Tools?
            .Where(t => !MetaToolBlocklist.Contains(t.Name)
                     && !(t.Name.Equals("Skill", StringComparison.OrdinalIgnoreCase) && !hasSlashCommand))
            .ToList();

        if (forwardableTools is { Count: > 0 })
        {
            out_.Tools = forwardableTools.Select(t => new OpenAiTool
            {
                Function = new OpenAiFunction
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.InputSchema,
                    Strict = t.Strict == true ? true : null
                }
            }).ToList();

            out_.ParallelToolCalls = true;

            // Env-level tool_choice override takes priority.
            var envChoice = _pipelineOpts.ToolChoice?.Trim().ToLowerInvariant();
            out_.ToolChoice = envChoice switch
            {
                "required" => (object)"required",
                "none" => "none",
                "auto" => "auto",
                _ => MapAnthropicToolChoice(req.ToolChoice)
            };
        }

        return out_;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IEnumerable<OpenAiMessage> TranslateUserTurn(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            var s = content.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(s))
                yield return new OpenAiMessage { Role = "user", Content = s };
            yield break;
        }

        if (content.ValueKind != JsonValueKind.Array) yield break;

        var userTextParts = new List<string>();

        foreach (var block in content.EnumerateArray())
        {
            var type = block.TryGetProperty("type", out var tp) ? tp.GetString() : null;

            if (type == "tool_result")
            {
                // Flush pending user text first.
                if (userTextParts.Count > 0)
                {
                    yield return new OpenAiMessage
                    {
                        Role = "user",
                        Content = string.Join("\n", userTextParts).TrimEnd()
                    };
                    userTextParts.Clear();
                }

                var id = block.TryGetProperty("tool_use_id", out var tid)
                    ? tid.GetString() ?? $"missing_{Guid.NewGuid():N}"
                    : $"missing_{Guid.NewGuid():N}";

                yield return new OpenAiMessage
                {
                    Role = "tool",
                    ToolCallId = id,
                    Content = StringifyToolResultContent(block)
                };
                continue;
            }

            if (type == "text" && block.TryGetProperty("text", out var tx))
                userTextParts.Add(tx.GetString() ?? string.Empty);
            else if (type == "image")
                userTextParts.Add("[image input omitted - translator does not bridge vision payloads]");
            else if (type is not null and not "text" and not "image")
                userTextParts.Add($"[{type} block omitted by translator]");
        }

        if (userTextParts.Count > 0)
            yield return new OpenAiMessage
            {
                Role = "user",
                Content = string.Join("\n", userTextParts).TrimEnd()
            };
    }

    private static OpenAiMessage TranslateAssistantTurn(JsonElement content)
    {
        var textParts = new List<string>();
        var toolCalls = new List<OpenAiToolCall>();

        var blocks = content.ValueKind == JsonValueKind.String
            ? new[] { JsonSerializer.SerializeToElement(new { type = "text", text = content.GetString() }) }.AsEnumerable()
            : content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray().AsEnumerable()
                : [];

        foreach (var block in blocks)
        {
            var type = block.TryGetProperty("type", out var tp) ? tp.GetString() : null;

            if (type == "text" && block.TryGetProperty("text", out var tx))
                textParts.Add(tx.GetString() ?? string.Empty);
            else if (type == "tool_use")
            {
                var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? $"call_{Guid.NewGuid():N}" : $"call_{Guid.NewGuid():N}";
                var name = block.TryGetProperty("name", out var nm) ? nm.GetString() ?? "unknown_tool" : "unknown_tool";
                var args = block.TryGetProperty("input", out var inp)
                    ? (inp.ValueKind == JsonValueKind.Object ? inp.GetRawText() : "{}")
                    : "{}";

                toolCalls.Add(new OpenAiToolCall
                {
                    Id = id,
                    Function = new OpenAiToolCallFunction { Name = name, Arguments = args }
                });
            }
        }

        var joinedText = string.Join(string.Empty, textParts);
        return new OpenAiMessage
        {
            Role = "assistant",
            Content = joinedText.Length > 0 ? joinedText : null,
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null
        };
    }

    private static string StringifyToolResultContent(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var c)) return string.Empty;
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? string.Empty;
        if (c.ValueKind == JsonValueKind.Array)
            return string.Join("\n", c.EnumerateArray()
                .Where(x => x.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(x => x.TryGetProperty("text", out var tx) ? tx.GetString() ?? string.Empty : string.Empty));
        if (c.ValueKind != JsonValueKind.Undefined && c.ValueKind != JsonValueKind.Null)
            return c.GetRawText();
        return string.Empty;
    }

    private static object? MapAnthropicToolChoice(AnthropicToolChoice? tc)
    {
        if (tc is null) return null;
        return tc.Type switch
        {
            "none" => (object)"none",
            "any" => "required",
            "tool" when tc.Name is not null => new { type = "function", function = new { name = tc.Name } },
            _ => "auto"
        };
    }

    private static int Clamp(int value, int cap) =>
        cap == int.MaxValue ? Math.Max(1, value) : Math.Clamp(value, 1, cap);
}
