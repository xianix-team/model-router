using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace LlmModelProxy.Infrastructure.Http;

/// <summary>
/// Convenience extension methods for adding standard Polly resilience pipelines
/// to named HTTP clients.
/// </summary>
public static class PollyHttpClientExtensions
{
    /// <summary>
    /// Adds a standard retry + circuit-breaker pipeline suitable for LLM API calls.
    /// Uses <c>Microsoft.Extensions.Http.Resilience</c> (Polly 8) built-in strategies.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/> to configure.</param>
    /// <param name="maxRetries">How many retries before failing (default 2).</param>
    public static IHttpClientBuilder AddLlmResiliencePipeline(
        this IHttpClientBuilder builder,
        int maxRetries = 2)
    {
        // AddStandardResilienceHandler returns IHttpStandardResiliencePipelineBuilder,
        // not IHttpClientBuilder — call it for side-effects and return the original builder.
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = maxRetries;
            options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            options.Retry.UseJitter = true;

            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(360);

            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.FailureRatio = 0.5;
            options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
        });

        return builder;
    }

    /// <summary>
    /// Adds a minimal pipeline for streaming clients: long timeout, no retries.
    /// </summary>
    public static IHttpClientBuilder AddLlmStreamingResiliencePipeline(
        this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 0;
            options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(10);
        });

        return builder;
    }
}
