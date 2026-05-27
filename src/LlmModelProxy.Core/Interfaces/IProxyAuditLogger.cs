using LlmModelProxy.Core.Models.Anthropic;

namespace LlmModelProxy.Core.Interfaces;

/// <summary>
/// Structured per-request audit log (replaces logger.mjs from the Node prototype).
/// </summary>
public interface IProxyAuditLogger
{
    void LogRequest(int reqId, AnthropicRequest request);
    void LogProviderCall(int reqId, string providerName, string upstreamModel, string toolChoice, int toolCount);
    void LogProviderStatus(int reqId, int statusCode, bool streaming);
    void LogToolCallDetected(int reqId, string toolName, string toolId);
    void LogDone(int reqId, string stopReason, bool hasText, int toolCount, bool taskCompleteTool);
    void LogError(int reqId, string label, Exception? ex = null, string? detail = null);
    int NextRequestId();
}
