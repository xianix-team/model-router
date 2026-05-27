using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using Microsoft.Extensions.Logging;

namespace LlmModelProxy.Providers.Anthropic;

/// <summary>
/// <see cref="ILlmProvider"/> that forwards requests verbatim to the real
/// Anthropic API.  Useful for routing specific Claude model IDs directly to
/// Anthropic while sending everything else to OpenAI or Ollama.
/// </summary>
public sealed class AnthropicPassthroughProvider : ILlmProvider
{
    private const string HttpClientName = "AnthropicPassthrough";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicPassthroughProvider> _logger;

    public string Name => "Anthropic";

    public AnthropicPassthroughProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicPassthroughProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool SupportsModel(string modelId) =>
        modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase);

    // ── Non-streaming ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Anthropic passthrough non-stream, model={Model}", request.Model);

        request.Stream = false;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var httpResponse = await client.PostAsJsonAsync(
            "/v1/messages", request, JsonOpts, cancellationToken);

        httpResponse.EnsureSuccessStatusCode();

        return await httpResponse.Content
            .ReadFromJsonAsync<AnthropicResponse>(JsonOpts, cancellationToken)
            ?? throw new InvalidOperationException("Anthropic returned an empty response body.");
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<AnthropicSseEvent> StreamAsync(
        AnthropicRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Anthropic passthrough stream, model={Model}", request.Model);

        request.Stream = true;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, JsonOpts),
                Encoding.UTF8,
                "application/json")
        };

        using var httpResponse = await client.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        httpResponse.EnsureSuccessStatusCode();

        // Pass Anthropic SSE events through unchanged.
        await using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new System.IO.StreamReader(stream, Encoding.UTF8);

        string? currentEvent = null;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                currentEvent = line["event: ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                var payload = line["data: ".Length..].Trim();
                if (string.IsNullOrEmpty(payload) || currentEvent is null) continue;

                JsonElement data;
                try
                {
                    data = JsonDocument.Parse(payload).RootElement.Clone();
                }
                catch (JsonException) { continue; }

                yield return new AnthropicSseEvent { Event = currentEvent, Data = data };
                currentEvent = null;
            }
        }
    }
}
