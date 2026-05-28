using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Routing;
using LlmModelProxy.Providers.Anthropic;
using LlmModelProxy.Providers.AzureOpenAI;
using LlmModelProxy.Providers.Ollama;
using LlmModelProxy.Providers.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Providers;

/// <summary>
/// Extension methods for registering all provider-layer services into the DI container.
/// </summary>
public static class ProviderServiceExtensions
{
    /// <summary>
    /// Registers all LLM providers, translators, the routing engine, and their HTTP clients.
    /// Call this from <c>Program.cs</c> after binding configuration.
    /// </summary>
    public static IServiceCollection AddLlmProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Options ───────────────────────────────────────────────────────────
        // PipelineOptions is a nested section — bind it explicitly so
        // IOptions<PipelineOptions> is resolvable inside the translators.
        services.Configure<PipelineOptions>(configuration.GetSection("Proxy:Pipeline"));
        services.Configure<OpenAiOptions>(configuration.GetSection(OpenAiOptions.Section));
        services.Configure<AzureOpenAiOptions>(configuration.GetSection(AzureOpenAiOptions.Section));
        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.Section));
        services.Configure<AnthropicOptions>(configuration.GetSection(AnthropicOptions.Section));

        // ── Shared translators ────────────────────────────────────────────────
        services.AddScoped<OpenAiRequestTranslator>();

        // ── HTTP clients ──────────────────────────────────────────────────────
        // Use the (IServiceProvider, HttpClient) overload so credentials are
        // resolved from IOptions<T> at client-creation time — not captured
        // as raw strings at registration time. This guarantees env vars,
        // user-secrets, and any other late-bound config sources are honoured.
        AddOpenAiHttpClient(services);
        AddAzureOpenAiHttpClient(services);
        AddOllamaHttpClient(services);
        AddAnthropicHttpClient(services);

        // ── Provider implementations ──────────────────────────────────────────
        services.AddScoped<ILlmProvider, OpenAiProvider>();
        services.AddScoped<ILlmProvider, AzureOpenAiProvider>();
        services.AddScoped<ILlmProvider, OllamaProvider>();
        services.AddScoped<ILlmProvider, AnthropicPassthroughProvider>();

        // ── Router ────────────────────────────────────────────────────────────
        services.AddScoped<IProviderRouter, ProviderRouter>();

        return services;
    }

    // ── HTTP client registrations ─────────────────────────────────────────────

    private static void AddOpenAiHttpClient(IServiceCollection services)
    {
        services.AddHttpClient("OpenAI", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<OpenAiOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "https://api.openai.com" : opts.BaseUrl;
            // Ensure trailing slash so relative request paths (e.g. "v1/chat/completions")
            // are appended correctly rather than replacing the last path segment.
            if (!baseUrl.EndsWith('/')) baseUrl += '/';
            client.BaseAddress = new Uri(baseUrl);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
        });
    }

    private static void AddAzureOpenAiHttpClient(IServiceCollection services)
    {
        services.AddHttpClient("AzureOpenAI", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AzureOpenAiOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(opts.Endpoint))
                client.BaseAddress = new Uri(opts.Endpoint);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                client.DefaultRequestHeaders.Add("api-key", opts.ApiKey);
        });
    }

    private static void AddOllamaHttpClient(IServiceCollection services)
    {
        services.AddHttpClient("Ollama", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = new Uri(
                string.IsNullOrWhiteSpace(opts.BaseUrl) ? "http://localhost:11434" : opts.BaseUrl);
        });
    }

    private static void AddAnthropicHttpClient(IServiceCollection services)
    {
        services.AddHttpClient("AnthropicPassthrough", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            client.BaseAddress = new Uri(
                string.IsNullOrWhiteSpace(opts.BaseUrl) ? "https://api.anthropic.com" : opts.BaseUrl);
            if (!string.IsNullOrWhiteSpace(opts.ApiKey))
                client.DefaultRequestHeaders.Add("x-api-key", opts.ApiKey);
            client.DefaultRequestHeaders.Add(
                "anthropic-version",
                string.IsNullOrWhiteSpace(opts.AnthropicVersion) ? "2023-06-01" : opts.AnthropicVersion);
        });
    }
}
