using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using ShelfGuard.Application.Services;

namespace ShelfGuard.Infrastructure.Resilience;

/// <summary>
/// Shared Polly pipeline for optional third-party HTTP dependencies (stage 2 of the resilience
/// plan): retry (GET-only clients) → circuit breaker → per-attempt timeout. A failing
/// dependency then fails fast instead of piling up slow requests behind the API.
/// </summary>
internal static class ExternalHttpResilience
{
    /// <param name="attemptTimeout">Per-attempt timeout (kept below the HttpClient's own).</param>
    /// <param name="maxRetries">Only use &gt; 0 for clients that send nothing but GET (Open-Meteo, Nominatim); POST clients (Checkbox) must pass 0 — a retried POST is not idempotent.</param>
    public static IHttpResiliencePipelineBuilder AddExternalResilience(
        this IHttpClientBuilder builder, string pipelineName, TimeSpan attemptTimeout, int maxRetries)
    {
        return builder.AddResilienceHandler(pipelineName, pipeline =>
        {
            if (maxRetries > 0)
            {
                var retry = new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = maxRetries,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    Delay = TimeSpan.FromMilliseconds(500),
                };
                pipeline.AddRetry(retry);
            }

            pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
            {
                SamplingDuration = TimeSpan.FromSeconds(30),
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
            });

            pipeline.AddTimeout(attemptTimeout);
        });
    }

    /// <summary>
    /// Outermost handler for dependencies the app can live without: turns "circuit open /
    /// timed out / unreachable" into one <see cref="ExternalServiceUnavailableException"/>.
    /// Register it BEFORE <see cref="AddExternalResilience"/> so it wraps the pipeline.
    /// </summary>
    public static IHttpClientBuilder AddUnavailableTranslation(this IHttpClientBuilder builder, string service) =>
        builder.AddHttpMessageHandler(() => new UnavailableTranslatingHandler(service));

    private sealed class UnavailableTranslatingHandler(string service) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (
                ex is BrokenCircuitException or TimeoutRejectedException or HttpRequestException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                throw new ExternalServiceUnavailableException(
                    service, $"External service '{service}' is unavailable.", ex);
            }
        }
    }
}
