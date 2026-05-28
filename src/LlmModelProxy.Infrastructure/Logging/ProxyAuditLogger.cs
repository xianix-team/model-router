using System.Text.Json;
using LlmModelProxy.Core.Configuration;
using LlmModelProxy.Core.Interfaces;
using LlmModelProxy.Core.Models.Anthropic;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace LlmModelProxy.Infrastructure.Logging;

/// <summary>
/// File-backed audit logger that emits human-readable per-request blocks,
/// matching the output format established by <c>logger.mjs</c> in the Node prototype.
/// </summary>
public sealed class ProxyAuditLogger : IProxyAuditLogger, IDisposable
{
    private static readonly JsonSerializerOptions JsonPrintOpts = new() { WriteIndented = false };

    private int _requestCounter;
    private readonly ILogger _auditLog;
    private readonly bool _enabled;

    public ProxyAuditLogger(IOptions<ProxyOptions> opts)
    {
        var logOpts = opts.Value.Logging;
        _enabled = logOpts.Enabled;

        var filePath = string.IsNullOrWhiteSpace(logOpts.FilePath)
            ? "proxy-audit.log"
            : logOpts.FilePath;

        _auditLog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                filePath,
                outputTemplate: "{Message:lj}{NewLine}",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                buffered: false)
            .CreateLogger();
    }

    public int NextRequestId() => Interlocked.Increment(ref _requestCounter);

    public void LogRequest(int reqId, AnthropicRequest request)
    {
        if (!_enabled) return;
        var snippet = request.Messages.LastOrDefault()?.TextSnippet(120) ?? "(no messages)";
        Write(reqId, "IN", $"model={request.Model} stream={request.Stream} tools={request.Tools?.Count ?? 0} | {snippet}");
    }

    public void LogProviderCall(int reqId, string providerName, string upstreamModel, string toolChoice, int toolCount)
    {
        if (!_enabled) return;
        Write(reqId, "OUT", $"provider={providerName} model={upstreamModel} tool_choice={toolChoice} tools={toolCount}");
    }

    public void LogProviderStatus(int reqId, int statusCode, bool streaming)
    {
        if (!_enabled) return;
        Write(reqId, "RES", $"status={statusCode} streaming={streaming}");
    }

    public void LogToolCallDetected(int reqId, string toolName, string toolId)
    {
        if (!_enabled) return;
        Write(reqId, "TOOL", $"name={toolName} id={toolId}");
    }

    public void LogDone(int reqId, string stopReason, bool hasText, int toolCount, bool taskCompleteTool)
    {
        if (!_enabled) return;
        var extra = taskCompleteTool ? " [__task_complete__ intercepted]" : string.Empty;
        Write(reqId, "DONE", $"stop_reason={stopReason} hasText={hasText} toolCalls={toolCount}{extra}");
        _auditLog.Information("================");
    }

    public void LogError(int reqId, string label, Exception? ex = null, string? detail = null)
    {
        if (!_enabled) return;

        var ts     = DateTime.UtcNow;
        var status = ex is HttpRequestException { StatusCode: { } sc } ? (int)sc : (int?)null;
        var msg    = detail ?? ex?.Message ?? "unknown error";
        var hint   = BuildHint(status, msg);

        _auditLog.Error(
            "[{Ts:HH:mm:ss.fff}] #{ReqId} ERROR\n" +
            "  where  : {Label}\n" +
            "  status : {Status}\n" +
            "  message: {Message}" +
            "{Hint}",
            ts, reqId,
            label,
            status.HasValue ? $"{status} ({(System.Net.HttpStatusCode)status.Value})" : "—",
            msg,
            hint is not null ? $"\n  hint   : {hint}" : "");

        _auditLog.Information("================");
    }

    private static string? BuildHint(int? status, string message) => status switch
    {
        404 => "Check that BaseUrl in appsettings points at the correct provider URL " +
               "AND that DefaultModel is a model the provider actually supports. " +
               "These are the two most common causes of a 404.",
        401 or 403 => "Authentication failed — verify ApiKey in appsettings.Local.json is " +
                      "valid and has not expired.",
        429 => "Rate limit hit — the provider is rejecting requests temporarily. " +
               "Wait before retrying or switch to a model tier with a higher quota.",
        >= 500 => "The provider returned a server-side error. " +
                  "Check provider status page or try a different model.",
        _ => message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            ? "TLS/SSL error — check that BaseUrl uses https:// and the provider certificate is valid."
            : null
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Write(int reqId, string stage, string message) =>
        _auditLog.Information("[{Ts:HH:mm:ss.fff}] #{ReqId} {Stage,-6} | {Message}",
            DateTime.UtcNow, reqId, stage, message);

    public void Dispose()
    {
        if (_auditLog is IDisposable d)
            d.Dispose();
    }
}
