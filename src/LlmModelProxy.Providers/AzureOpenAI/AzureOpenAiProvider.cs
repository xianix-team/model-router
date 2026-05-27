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

namespace LlmModelProxy.Providers.AzureOpenAI;

/// <summary>
/// <see cref="ILlmProvider"/> that forwards to Azure OpenAI Service.
/// Azure uses a different URL scheme (<c>/openai/deployments/{deployment}/chat/completions</c>)
/// and authenticates via <c>api-key</c> header instead of Bearer token.
/// All translation logic is reused from the OpenAI provider.
/// </summary>
public sealed class AzureOpenAiProvider : ILlmProvider
{
    private const string HttpClientName = "AzureOpenAI";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpenAiRequestTranslator _requestTranslator;
    private readonly AzureOpenAiOptions _opts;
    private readonly ILogger<AzureOpenAiProvider> _logger;

    public string Name => "AzureOpenAI";

    public AzureOpenAiProvider(
        IHttpClientFactory httpClientFactory,
        OpenAiRequestTranslator requestTranslator,
        IOptions<AzureOpenAiOptions> opts,
        ILogger<AzureOpenAiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _requestTranslator = requestTranslator;
        _opts = opts.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool SupportsModel(string modelId) =>
        !string.IsNullOrEmpty(_opts.DeploymentName) &&
        modelId.Equals(_opts.DeploymentName, StringComparison.OrdinalIgnoreCase);

    // ── Non-streaming ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken = default)
    {
        var openAiRequest = _requestTranslator.Translate(request);
        openAiRequest.Stream = false;

        _logger.LogDebug("AzureOpenAI non-stream request, deployment={Deployment}", _opts.DeploymentName);

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var httpResponse = await client.PostAsJsonAsync(
            ChatCompletionsPath(), openAiRequest, cancellationToken);

        httpResponse.EnsureSuccessStatusCode();

        var openAiResponse = await httpResponse.Content
            .ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Azure OpenAI returned an empty response body.");

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

        _logger.LogDebug("AzureOpenAI stream request, deployment={Deployment}", _opts.DeploymentName);

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatCompletionsPath())
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ChatCompletionsPath() =>
        $"/openai/deployments/{_opts.DeploymentName}/chat/completions?api-version={_opts.ApiVersion}";

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
