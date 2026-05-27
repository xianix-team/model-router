namespace LlmModelProxy.Api.Middleware;

/// <summary>
/// Logs every inbound HTTP request (method + path + status + elapsed ms)
/// at DEBUG level via the standard Microsoft.Extensions.Logging pipeline.
/// This is a lightweight alternative to UseSerilogRequestLogging() for cases
/// where the full Serilog enrichment is not needed.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            _logger.LogDebug("{Method} {Path} → {Status} in {Elapsed}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds);
        }
    }
}
