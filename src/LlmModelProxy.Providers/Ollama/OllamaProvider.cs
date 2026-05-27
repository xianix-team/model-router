using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Providers.OpenAI;
using LlmModelProxy.Providers.OpenAI.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Providers.Ollama;

/// <summary>
/// <see cref="ILlmProvider"/> that targets a local or remote Ollama instance.
/// Ollama exposes an OpenAI-compatible <c>/v1/chat/completions</c> endpoint,
/// so we reuse all OpenAI translators — only the HTTP client base address differs.
/// </summary>
public sealed class OllamaProvider : ILlmProvider
{
    private const string HttpClientName = "Ollama";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiRequestTranslator _requestTranslator;
    private readonly OllamaOptions _opts;
    private readonly ILogger<OllamaProvider> _logger;

    public string Name => "Ollama";

    public OllamaProvider(
        IHttpClientFactory httpClientFactory,
        OpenAiRequestTranslator requestTranslator,
        IOptions<OllamaOptions> opts,
        ILogger<OllamaProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _requestTranslator = requestTranslator;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool SupportsModel(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return false;
        var prefixes = _opts.ModelPrefixes
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return prefixes.Any(p => modelId.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    // ── Non-streaming ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken = default)
    {
        var openAiRequest = _requestTranslator.Translate(request);
        // Override model with the Ollama-specific tag when generic name used.
        if (string.IsNullOrEmpty(openAiRequest.Model))
            openAiRequest.Model = _opts.DefaultModel;
        openAiRequest.Stream = false;

        _logger.LogDebug("Ollama non-stream request to model {Model}", openAiRequest.Model);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var httpResponse = await client.PostAsJsonAsync(
            "/v1/chat/completions", openAiRequest, cancellationToken);

        httpResponse.EnsureSuccessStatusCode();

        var openAiResponse = await httpResponse.Content
            .ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Ollama returned an empty response body.");

        return OpenAiResponseTranslator.Translate(openAiResponse, request.Model);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<AnthropicSseEvent> StreamAsync(
        AnthropicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var openAiRequest = _requestTranslator.Translate(request);
        if (string.IsNullOrEmpty(openAiRequest.Model))
            openAiRequest.Model = _opts.DefaultModel;
        openAiRequest.Stream = true;
        // Ollama supports stream_options.include_usage from v0.3+
        openAiRequest.StreamOptions = new OpenAiStreamOptions { IncludeUsage = true };

        _logger.LogDebug("Ollama stream request to model {Model}", openAiRequest.Model);

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(openAiRequest),
                Encoding.UTF8,
                "application/json")
        };

        using var httpResponse = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        httpResponse.EnsureSuccessStatusCode();

        var messageId = $"msg_{Guid.NewGuid():N}";
        var chunks = ReadChunksAsync(httpResponse, cancellationToken);

        await foreach (var evt in OpenAiStreamTranslator.TranslateAsync(chunks, request.Model, messageId)
                           .WithCancellation(cancellationToken))
        {
            yield return evt;
        }
    }

    // ── SSE reader ────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<OpenAiStreamChunk> ReadChunksAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;
            if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

            var payload = line["data: ".Length..].Trim();
            if (payload == "[DONE]") break;
            if (string.IsNullOrEmpty(payload)) continue;

            OpenAiStreamChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(payload); }
            catch (JsonException) { continue; }

            if (chunk is not null)
                yield return chunk;
        }
    }
}
