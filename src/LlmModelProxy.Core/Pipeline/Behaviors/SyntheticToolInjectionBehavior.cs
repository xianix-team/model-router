using System.Text.Json;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Core.Pipeline.Behaviors;

/// <summary>
/// When <see cref="PipelineOptions.ToolChoice"/> is "required", injects the
/// synthetic <c>__task_complete__</c> tool so the model always has a safe
/// exit path instead of being forced to call a real tool when the task is done.
/// </summary>
public sealed class SyntheticToolInjectionBehavior : IPipelineBehavior
{
    public const string SyntheticToolName = "__task_complete__";

    private static readonly AnthropicTool SyntheticTool = new()
    {
        Name = SyntheticToolName,
        Description =
            "Call this tool ONLY when the entire task has been fully completed and no more tool " +
            "calls are needed. Pass the final result or summary as `result`. Do NOT call this mid-task.",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                result = new
                {
                    type = "string",
                    description = "Concise summary of what was accomplished, or the error/blocker if the task failed."
                }
            },
            required = new[] { "result" }
        })
    };

    private readonly PipelineOptions _options;

    public int Order => 20;

    public SyntheticToolInjectionBehavior(IOptions<PipelineOptions> options)
    {
        _options = options.Value;
    }

    public Task ExecuteAsync(AnthropicRequest request, CancellationToken cancellationToken = default)
    {
        // Only inject when tool_choice will be forced to "required" and tools are present.
        if (!string.Equals(_options.ToolChoice, "required", StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        if (request.Tools is null || request.Tools.Count == 0)
            return Task.CompletedTask;

        // Avoid duplicates if the behavior runs more than once.
        if (!request.Tools.Any(t => t.Name == SyntheticToolName))
            request.Tools.Add(SyntheticTool);

        return Task.CompletedTask;
    }
}
