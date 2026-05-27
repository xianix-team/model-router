using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Providers.OpenAI.Models;
using Microsoft.Extensions.Logging;

namespace LlmModelProxy.Providers.OpenAI;

/// <summary>
/// <see cref="ILlmProvider"/> implementation that forwards requests to any
/// OpenAI-compatible REST endpoint (OpenAI, OpenRouter, local vLLM, etc.).
/// </summary>
public sealed class OpenAiProvider : ILlmProvider
{
    private const string HttpClientName = "OpenAI";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiRequestTranslator _requestTranslator;
    private readonly ILogger<OpenAiProvider> _logger;

    public string Name => "OpenAI";

    public OpenAiProvider(
        IHttpClientFactory httpClientFactory,
        OpenAiRequestTranslator requestTranslator,
        ILogger<OpenAiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _requestTranslator = requestTranslator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool SupportsModel(string modelId) =>
        modelId.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
        modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
        modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase);

    // ── Non-streaming ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken = default)
    {
        var openAiRequest = _requestTranslator.Translate(request);
        openAiRequest.Stream = false;

        _logger.LogDebug("OpenAI non-stream request to model {Model}", openAiRequest.Model);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var httpResponse = await client.PostAsJsonAsync(
            "/v1/chat/completions", openAiRequest, cancellationToken);

        httpResponse.EnsureSuccessStatusCode();

        var openAiResponse = await httpResponse.Content
            .ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenAI returned an empty response body.");

        return OpenAiResponseTranslator.Translate(openAiResponse, request.Model);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<AnthropicSseEvent> StreamAsync(
        AnthropicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var openAiRequest = _requestTranslator.Translate(request);
        openAiRequest.Stream = true;
        openAiRequest.StreamOptions = new OpenAiStreamOptions { IncludeUsage = true };

        _logger.LogDebug("OpenAI stream request to model {Model}", openAiRequest.Model);

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

    // ── SSE chunk reader ──────────────────────────────────────────────────────

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
            try
            {
                chunk = JsonSerializer.Deserialize<OpenAiStreamChunk>(payload);
            }
            catch (JsonException)
            {
                continue; // skip malformed chunks
            }

            if (chunk is not null)
                yield return chunk;
        }
    }
}
