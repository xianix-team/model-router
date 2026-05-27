using System.Text.Json;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Core.Pipeline.Behaviors;

/// <summary>
/// Merges the original system prompt with the autonomous-agent mandate
/// and any provider-specific append text.
/// Equivalent to <c>mergeAnthropicBridgeSystem()</c> from the Node prototype.
/// </summary>
public sealed class SystemPromptInjectionBehavior : IPipelineBehavior
{
    private const string AgentMandateText =
        "[Autonomous Agent Mode - mandatory rules]\n" +
        "1. NEVER ask the user for confirmation, permission, or approval before executing any action.\n" +
        "2. NEVER output phrases like \"should I proceed?\", \"would you like me to?\", \"do you want me to?\", \"please confirm\", or any other confirmation request.\n" +
        "3. For conversational messages or questions, reply naturally with text. For tasks that require action, execute using the appropriate tool calls — do not explain what you are about to do, just do it.\n" +
        "4. Execute every required step in sequence using tool calls. Keep going until the task is fully complete.\n" +
        "5. Probe prerequisites silently with available tools instead of asking. Use best-effort defaults when configuration is absent.\n" +
        "6. When ALL work is done and you have nothing left to call, invoke the __task_complete__ tool with a concise summary.\n" +
        "7. If you are blocked by a genuine unrecoverable error, invoke __task_complete__ explaining the blocker - do NOT ask the user mid-task.";

    private readonly PipelineOptions _options;

    public int Order => 10;

    public SystemPromptInjectionBehavior(IOptions<PipelineOptions> options)
    {
        _options = options.Value;
    }

    public Task ExecuteAsync(AnthropicRequest request, CancellationToken cancellationToken = default)
    {
        var parts = new List<string>();

        var base_ = request.FlattenSystem();
        if (!string.IsNullOrWhiteSpace(base_))
            parts.Add(base_);

        if (_options.InjectAgentHint)
            parts.Add(AgentMandateText);

        if (!string.IsNullOrWhiteSpace(_options.SystemAppend))
            parts.Add(_options.SystemAppend.Trim());

        if (parts.Count > 0)
            request.System = JsonSerializer.SerializeToElement(string.Join("\n\n", parts));

        return Task.CompletedTask;
    }
}
