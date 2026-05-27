using System.Text.Json;
using FluentAssertions;
using Xunit;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Core.Pipeline.Behaviors;
using LlmModelProxy.Providers.OpenAI;
using LlmModelProxy.Providers.OpenAI.Models;

namespace LlmModelProxy.Unit.Tests.OpenAi;

public sealed class OpenAiResponseTranslatorTests
{
    // ── MapStopReason ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("tool_calls",    "tool_use")]
    [InlineData("function_call", "tool_use")]
    [InlineData("length",        "max_tokens")]
    [InlineData("stop",          "end_turn")]
    [InlineData(null,            "end_turn")]
    [InlineData("unknown",       "end_turn")]
    public void MapStopReason_MapsCorrectly(string? input, string expected)
    {
        OpenAiResponseTranslator.MapStopReason(input).Should().Be(expected);
    }

    // ── SafeParseJsonObject ───────────────────────────────────────────────────

    [Fact]
    public void SafeParseJsonObject_NullInput_ReturnsEmptyObject()
    {
        var result = OpenAiResponseTranslator.SafeParseJsonObject(null);
        result.ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void SafeParseJsonObject_ValidObject_ReturnsItUnchanged()
    {
        var json = """{"key":"value"}""";
        var result = OpenAiResponseTranslator.SafeParseJsonObject(json);
        result.TryGetProperty("key", out var val).Should().BeTrue();
        val.GetString().Should().Be("value");
    }

    [Fact]
    public void SafeParseJsonObject_InvalidJson_ReturnsRawWrapper()
    {
        var result = OpenAiResponseTranslator.SafeParseJsonObject("not json");
        result.TryGetProperty("_raw_arguments", out _).Should().BeTrue();
    }

    [Fact]
    public void SafeParseJsonObject_NonObjectJson_ReturnsRawWrapper()
    {
        var result = OpenAiResponseTranslator.SafeParseJsonObject("[1,2,3]");
        result.TryGetProperty("_raw", out _).Should().BeTrue();
    }

    // ── ExtractTaskCompleteResult ─────────────────────────────────────────────

    [Fact]
    public void ExtractTaskCompleteResult_NullList_ReturnsNull()
    {
        OpenAiResponseTranslator.ExtractTaskCompleteResult(null).Should().BeNull();
    }

    [Fact]
    public void ExtractTaskCompleteResult_EmptyList_ReturnsNull()
    {
        OpenAiResponseTranslator.ExtractTaskCompleteResult([]).Should().BeNull();
    }

    [Fact]
    public void ExtractTaskCompleteResult_OtherTool_ReturnsNull()
    {
        var calls = new List<OpenAiToolCall>
        {
            new() { Id = "1", Function = new() { Name = "bash", Arguments = "{}" } }
        };
        OpenAiResponseTranslator.ExtractTaskCompleteResult(calls).Should().BeNull();
    }

    [Fact]
    public void ExtractTaskCompleteResult_SyntheticTool_ReturnsResult()
    {
        var calls = new List<OpenAiToolCall>
        {
            new()
            {
                Id = "1",
                Function = new()
                {
                    Name = SyntheticToolInjectionBehavior.SyntheticToolName,
                    Arguments = """{"result":"Task completed successfully"}"""
                }
            }
        };
        OpenAiResponseTranslator.ExtractTaskCompleteResult(calls)
            .Should().Be("Task completed successfully");
    }

    // ── Translate (end-to-end) ────────────────────────────────────────────────

    [Fact]
    public void Translate_TextResponse_ReturnsMappedContentAndStopReason()
    {
        var response = new OpenAiChatResponse
        {
            Choices =
            [
                new OpenAiChoice
                {
                    Message = new OpenAiMessage { Role = "assistant", Content = "Hello world" },
                    FinishReason = "stop"
                }
            ],
            Usage = new OpenAiUsage { PromptTokens = 10, CompletionTokens = 5 }
        };

        var result = OpenAiResponseTranslator.Translate(response, "claude-sonnet-4-6");

        result.Model.Should().Be("claude-sonnet-4-6");
        result.StopReason.Should().Be("end_turn");
        result.Content.Should().HaveCount(1);
        result.Content[0].Type.Should().Be("text");
        result.Content[0].Text.Should().Be("Hello world");
        result.Usage.InputTokens.Should().Be(10);
        result.Usage.OutputTokens.Should().Be(5);
    }

    [Fact]
    public void Translate_TaskCompleteTool_ReturnsEndTurnTextBlock()
    {
        var response = new OpenAiChatResponse
        {
            Choices =
            [
                new OpenAiChoice
                {
                    Message = new OpenAiMessage
                    {
                        Role = "assistant",
                        ToolCalls =
                        [
                            new OpenAiToolCall
                            {
                                Id = "call_abc",
                                Function = new()
                                {
                                    Name = SyntheticToolInjectionBehavior.SyntheticToolName,
                                    Arguments = """{"result":"All done!"}"""
                                }
                            }
                        ]
                    },
                    FinishReason = "tool_calls"
                }
            ]
        };

        var result = OpenAiResponseTranslator.Translate(response, "claude-haiku-4-5-20251001");

        result.StopReason.Should().Be("end_turn");
        result.Content.Should().HaveCount(1);
        result.Content[0].Type.Should().Be("text");
        result.Content[0].Text.Should().Be("All done!");
    }
}
