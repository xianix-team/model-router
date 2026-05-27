using System.Text;
using System.Text.Json;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using LlmModelProxy.Core.Pipeline;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LlmModelProxy.Api.Controllers;

/// <summary>
/// Implements the Anthropic <c>POST /v1/messages</c> endpoint.
/// Accepts an <see cref="AnthropicRequest"/>, runs it through the behaviour
/// pipeline, routes it to the appropriate provider, and returns either a
/// buffered JSON response or an SSE stream — matching Anthropic's wire format
/// exactly so Claude CLI works without modification.
/// </summary>
[ApiController]
[Route("v1")]
public sealed class MessagesController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ProxyPipeline _pipeline;
    private readonly IProviderRouter _router;
    private readonly IProxyAuditLogger _audit;
    private readonly IOptions<PipelineOptions> _pipelineOpts;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(
        ProxyPipeline pipeline,
        IProviderRouter router,
        IProxyAuditLogger audit,
        IOptions<PipelineOptions> pipelineOpts,
        ILogger<MessagesController> logger)
    {
        _pipeline = pipeline;
        _router = router;
        _audit = audit;
        _pipelineOpts = pipelineOpts;
        _logger = logger;
    }

    [HttpPost("messages")]
    public async Task Messages(
        [FromBody] AnthropicRequest request,
        CancellationToken cancellationToken)
    {
        var reqId = _audit.NextRequestId();
        _audit.LogRequest(reqId, request);

        // Run pre-processing pipeline (system prompt injection, tool injection …)
        await _pipeline.ExecuteAsync(request, cancellationToken);

        var provider = _router.Route(request.Model);
        // Log the effective tool_choice that will be sent to the provider,
        // not the raw incoming value from the Anthropic client.
        var effectiveToolChoice = request.Tools is { Count: > 0 }
            ? (_pipelineOpts.Value.ToolChoice?.Trim().ToLowerInvariant() ?? "auto")
            : "n/a";
        _audit.LogProviderCall(reqId, provider.Name, request.Model,
            effectiveToolChoice, request.Tools?.Count ?? 0);

        if (request.Stream)
        {
            await HandleStreamAsync(request, provider, reqId, cancellationToken);
        }
        else
        {
            await HandleCompleteAsync(request, provider, reqId, cancellationToken);
        }
    }

    // ── Non-streaming ─────────────────────────────────────────────────────────

    private async Task HandleCompleteAsync(
        AnthropicRequest request,
        ILlmProvider provider,
        int reqId,
        CancellationToken ct)
    {
        AnthropicResponse response;
        try
        {
            response = await provider.CompleteAsync(request, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _audit.LogError(reqId, "provider.CompleteAsync [rate-limited]", ex);
            var waitSecs = ex.Data["RetryAfterSeconds"] as int? ?? 60;
            Response.StatusCode = 429;
            Response.Headers["Retry-After"] = waitSecs.ToString();
            await Response.WriteAsJsonAsync(
                new { type = "error", error = new { type = "rate_limit_error", message = ex.Message } },
                ct);
            return;
        }
        catch (Exception ex)
        {
            _audit.LogError(reqId, "provider.CompleteAsync", ex);
            Response.StatusCode = 502;
            await Response.WriteAsJsonAsync(
                new { type = "error", error = new { type = "api_error", message = ex.Message } },
                ct);
            return;
        }

        _audit.LogProviderStatus(reqId, 200, false);

        var hasText = response.Content.Any(c => c.Type == "text");
        var toolCount = response.Content.Count(c => c.Type == "tool_use");
        var isTaskComplete = response.Content.Any(c =>
            c.Type == "text" &&
            // If __task_complete__ was intercepted it became a text block.
            response.StopReason == "end_turn");

        _audit.LogDone(reqId, response.StopReason ?? "end_turn", hasText, toolCount, isTaskComplete);

        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(response, JsonOpts, ct);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    private async Task HandleStreamAsync(
        AnthropicRequest request,
        ILlmProvider provider,
        int reqId,
        CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        _audit.LogProviderStatus(reqId, 200, true);

        var toolsSeen = new HashSet<string>();
        string lastStopReason = "end_turn";

        try
        {
            await foreach (var evt in provider.StreamAsync(request, ct))
            {
                var wire = evt.ToWire(JsonOpts);
                await Response.WriteAsync(wire, Encoding.UTF8, ct);
                await Response.Body.FlushAsync(ct);

                // Track tool calls for audit.
                if (evt.Event == "content_block_start")
                {
                    var raw = JsonSerializer.Serialize(evt.Data, JsonOpts);
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("content_block", out var cb) &&
                        cb.TryGetProperty("type", out var tp) && tp.GetString() == "tool_use")
                    {
                        var name = cb.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                        var id = cb.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        if (!toolsSeen.Contains(id))
                        {
                            toolsSeen.Add(id);
                            _audit.LogToolCallDetected(reqId, name, id);
                        }
                    }
                }

                if (evt.Event == "message_delta")
                {
                    var raw = JsonSerializer.Serialize(evt.Data, JsonOpts);
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("delta", out var d) &&
                        d.TryGetProperty("stop_reason", out var sr))
                        lastStopReason = sr.GetString() ?? "end_turn";
                }
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            _audit.LogError(reqId, "provider.StreamAsync", ex);
            // Best-effort error event.
            var errEvent = $"event: error\ndata: {{\"type\":\"error\",\"error\":{{\"type\":\"api_error\",\"message\":\"{ex.Message}\"}}}}\n\n";
            await Response.WriteAsync(errEvent, ct);
        }

        _audit.LogDone(reqId, lastStopReason, hasText: true, toolCount: toolsSeen.Count, taskCompleteTool: false);
    }
}
