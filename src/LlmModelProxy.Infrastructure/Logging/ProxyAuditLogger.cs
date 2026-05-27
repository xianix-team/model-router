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
        var msg = detail ?? ex?.Message ?? "unknown error";
        _auditLog.Error("[{Ts:HH:mm:ss.fff}] #{ReqId} ERROR | {Label}: {Msg}",
            DateTime.UtcNow, reqId, label, msg);
        if (ex is not null)
            _auditLog.Error(ex, "  Stack trace:");
        _auditLog.Information("================");
    }

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
