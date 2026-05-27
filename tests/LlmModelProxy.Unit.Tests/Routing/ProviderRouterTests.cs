using FluentAssertions;
using Xunit;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Core.Routing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LlmModelProxy.Unit.Tests.Routing;

public sealed class ProviderRouterTests
{
    private static ILlmProvider MakeProvider(string name, bool supports = false)
    {
        var p = Substitute.For<ILlmProvider>();
        p.Name.Returns(name);
        p.SupportsModel(Arg.Any<string>()).Returns(supports);
        return p;
    }

    [Fact]
    public void Route_MatchesPrefixRule_ReturnsNamedProvider()
    {
        var openAi = MakeProvider("OpenAI");
        var ollama = MakeProvider("Ollama");

        var options = Options.Create(new ProxyOptions
        {
            ProviderRouting = new ProviderRoutingOptions
            {
                Rules = [new RoutingRule { ModelPrefix = "claude-", Provider = "OpenAI" }]
            }
        });

        var router = new ProviderRouter([openAi, ollama], options);

        router.Route("claude-sonnet-4-6").Should().BeSameAs(openAi);
    }

    [Fact]
    public void Route_WildcardRule_ReturnsCatchAll()
    {
        var openAi = MakeProvider("OpenAI");

        var options = Options.Create(new ProxyOptions
        {
            ProviderRouting = new ProviderRoutingOptions
            {
                Rules = [new RoutingRule { ModelPrefix = "*", Provider = "OpenAI" }]
            }
        });

        var router = new ProviderRouter([openAi], options);

        router.Route("some-unknown-model").Should().BeSameAs(openAi);
    }

    [Fact]
    public void Route_NoMatchingRule_FallsBackToSupportsModel()
    {
        var openAi = MakeProvider("OpenAI", supports: false);
        var ollama = MakeProvider("Ollama", supports: true);

        var options = Options.Create(new ProxyOptions
        {
            ProviderRouting = new ProviderRoutingOptions { Rules = [] }
        });

        var router = new ProviderRouter([openAi, ollama], options);

        router.Route("llama3:8b").Should().BeSameAs(ollama);
    }

    [Fact]
    public void Route_NoProviders_Throws()
    {
        var options = Options.Create(new ProxyOptions
        {
            ProviderRouting = new ProviderRoutingOptions { Rules = [] }
        });

        var router = new ProviderRouter([], options);

        var act = () => router.Route("any-model");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No provider found*");
    }
}
