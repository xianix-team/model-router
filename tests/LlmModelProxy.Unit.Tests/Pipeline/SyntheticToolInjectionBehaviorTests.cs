using System.Text.Json;
using FluentAssertions;
using Xunit;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Core.Pipeline.Behaviors;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Unit.Tests.Pipeline;

public sealed class SyntheticToolInjectionBehaviorTests
{
    private static SyntheticToolInjectionBehavior Create(string toolChoice = "required") =>
        new(Options.Create(new PipelineOptions { ToolChoice = toolChoice }));

    [Fact]
    public async Task Execute_ToolChoiceRequired_WithTools_InjectsSyntheticTool()
    {
        var behavior = Create("required");
        var request = new AnthropicRequest
        {
            Model = "claude-sonnet-4-6",
            Tools =
            [
                new AnthropicTool
                {
                    Name = "bash",
                    Description = "Run bash commands",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } })
                }
            ]
        };

        await behavior.ExecuteAsync(request, CancellationToken.None);

        request.Tools.Should().HaveCount(2);
        request.Tools!.Last().Name.Should().Be(SyntheticToolInjectionBehavior.SyntheticToolName);
    }

    [Fact]
    public async Task Execute_ToolChoiceAuto_DoesNotInject()
    {
        var behavior = Create("auto");
        var request = new AnthropicRequest
        {
            Model = "claude-sonnet-4-6",
            Tools =
            [
                new AnthropicTool { Name = "bash", InputSchema = default }
            ]
        };

        await behavior.ExecuteAsync(request, CancellationToken.None);

        request.Tools.Should().HaveCount(1);
    }

    [Fact]
    public async Task Execute_NoTools_DoesNotInject()
    {
        var behavior = Create("required");
        var request = new AnthropicRequest { Model = "claude-sonnet-4-6" };

        await behavior.ExecuteAsync(request, CancellationToken.None);

        request.Tools.Should().BeNullOrEmpty();
    }
}
